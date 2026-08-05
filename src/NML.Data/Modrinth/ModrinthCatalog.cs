using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using NML.Core.Download;

namespace NML.Data.Modrinth;

/// <summary>
/// <see cref="IModCatalog"/> backed by the Modrinth v2 API (api.modrinth.com/v2). No API key
/// required for read-only search/project/file access; respects the 300 req/min per-IP limit
/// (handled by the throttling HttpClient; this layer just calls endpoints).
/// </summary>
public sealed class ModrinthCatalog : IModCatalog
{
    private const string BaseUrl = "https://api.modrinth.com/v2";

    private readonly IHttpFetcher _http;
    private readonly ILogger<ModrinthCatalog> _logger;

    public ModrinthCatalog(IHttpFetcher http, ILogger<ModrinthCatalog> logger)
    {
        _http = http;
        _logger = logger;
    }

    public ModCatalogKind Kind => ModCatalogKind.Modrinth;

    /// <summary>Search modpacks (project_type:modpack) instead of mods. For the modpack browser.</summary>
    public async Task<IReadOnlyList<ModSearchResult>> SearchModpacksAsync(
        string query, int limit = 20, CancellationToken ct = default)
    {
        var facets = new List<string> { "project_type:modpack" };

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["query"] = query;
        qs["limit"] = limit.ToString();
        qs["facets"] = "[" + string.Join(",", facets.Select(f => $"[\"{f}\"]")) + "]";

        string url = $"{BaseUrl}/search?{qs}";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");

        var results = new List<ModSearchResult>();
        foreach (var h in hits.EnumerateArray())
        {
            results.Add(new ModSearchResult
            {
                ProjectId = GetString(h, "project_id"),
                Slug = GetString(h, "slug"),
                Title = GetString(h, "title"),
                Description = GetString(h, "description"),
                Author = GetString(h, "author"),
                Downloads = GetLong(h, "downloads"),
                Categories = GetList(h, "categories"),
                IconUrl = GetString(h, "icon_url"),
                Source = ModCatalogKind.Modrinth,
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<ModSearchResult>> SearchAsync(
        string query, string? gameVersion = null, ModLoader? loader = null,
        int limit = 20, CancellationToken ct = default)
    {
        var facets = new List<string>();

        // project_type:mod excludes modpacks/resourcepacks/shaders from the results.
        facets.Add("project_type:mod");
        if (gameVersion is not null) facets.Add($"versions:{gameVersion}");
        if (loader is not null && loader != ModLoader.Any)
            facets.Add($"categories:{LoaderFacet(loader.Value)}");

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["query"] = query;
        qs["limit"] = limit.ToString();
        qs["index"] = "relevance";
        if (facets.Count > 0)
            qs["facets"] = "[" + string.Join(",", facets.Select(f => $"[\"{f}\"]")) + "]";

        string url = $"{BaseUrl}/search?{qs}";
        _logger.LogDebug("Modrinth search: {Url}", url);

        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");

        var results = new List<ModSearchResult>();
        foreach (var h in hits.EnumerateArray())
        {
            results.Add(new ModSearchResult
            {
                ProjectId = GetString(h, "project_id"),
                Slug = GetString(h, "slug"),
                Title = GetString(h, "title"),
                Description = GetString(h, "description"),
                Author = GetString(h, "author"),
                Downloads = GetLong(h, "downloads"),
                Categories = GetList(h, "categories"),
                IconUrl = GetString(h, "icon_url"),
                Source = ModCatalogKind.Modrinth,
            });
        }
        return results;
    }

    public async Task<ModProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        string url = $"{BaseUrl}/project/{projectId}";
        string json;
        try { json = await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        return new ModProject
        {
            ProjectId = GetString(r, "id"),
            Slug = GetString(r, "slug"),
            Title = GetString(r, "title"),
            Description = GetString(r, "description"),
            Body = GetString(r, "body"),
            Categories = GetList(r, "categories"),
            GameVersions = GetList(r, "game_versions"),
            Loaders = GetList(r, "loaders").Select(ParseLoader).Where(l => l != ModLoader.Any).ToList(),
            IconUrl = GetString(r, "icon_url"),
            Source = ModCatalogKind.Modrinth,
        };
    }

    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(
        string projectId, string gameVersion, ModLoader loader, CancellationToken ct = default)
    {
        // Modrinth returns ALL version objects for a project; we filter client-side.
        string url = $"{BaseUrl}/project/{projectId}/version";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        string loaderFacet = LoaderFacet(loader);
        var files = new List<ModFile>();
        foreach (var ver in doc.RootElement.EnumerateArray())
        {
            var versions = GetList(ver, "game_versions");
            var loaders = GetList(ver, "loaders");
            if (!versions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase)) continue;
            if (!loaders.Contains(loaderFacet, StringComparer.OrdinalIgnoreCase)) continue;

            // The first "primary" file (or the first file) is the one to download.
            JsonElement filesArr = ver.GetProperty("files");
            foreach (var f in filesArr.EnumerateArray())
            {
                bool primary = f.TryGetProperty("primary", out var p) && p.GetBoolean();
                if (!primary && filesArr.EnumerateArray().Any(x =>
                        x.TryGetProperty("primary", out var pp) && pp.GetBoolean()))
                    continue; // skip non-primary when a primary exists

                string? sha1 = f.TryGetProperty("hashes", out var hashes) &&
                               hashes.TryGetProperty("sha1", out var s) ? s.GetString() : null;

                files.Add(new ModFile
                {
                    FileName = GetString(f, "filename"),
                    DownloadUrl = GetString(f, "url"),
                    Sha1 = sha1,
                    Size = GetLong(f, "size"),
                    GameVersion = gameVersion,
                    Loader = loader,
                    Source = ModCatalogKind.Modrinth,
                });
                break; // one file per matching version
            }
        }
        return files;
    }

    private static string LoaderFacet(ModLoader l) => l switch
    {
        ModLoader.Fabric => "fabric",
        ModLoader.Forge => "forge",
        ModLoader.Quilt => "quilt",
        ModLoader.NeoForge => "neoforge",
        _ => string.Empty,
    };

    private static ModLoader ParseLoader(string s) => s.ToLowerInvariant() switch
    {
        "fabric" => ModLoader.Fabric,
        "forge" => ModLoader.Forge,
        "quilt" => ModLoader.Quilt,
        "neoforge" => ModLoader.NeoForge,
        _ => ModLoader.Any,
    };

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64() : 0;

    private static IReadOnlyList<string> GetList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var i in v.EnumerateArray())
            if (i.ValueKind == JsonValueKind.String) list.Add(i.GetString() ?? string.Empty);
        return list;
    }
}
