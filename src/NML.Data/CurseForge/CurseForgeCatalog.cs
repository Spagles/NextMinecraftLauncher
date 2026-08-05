using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Modpacks;

namespace NML.Data.CurseForge;

/// <summary>
/// <see cref="IModCatalog"/> backed by the CurseForge API (api.curseforge.com). Requires a
/// user-supplied API key (from curseforge.com developer console) sent as the <c>x-api-key</c>
/// header. gameId 432 = Minecraft, classId 6 = mods.
/// </summary>
public sealed class CurseForgeCatalog : IModCatalog, ICurseForgeFileResolver
{
    private const string BaseUrl = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;
    private const int ModsClassId = 6;

    private readonly IHttpFetcher _http;
    private readonly string _apiKey;
    private readonly ILogger<CurseForgeCatalog> _logger;

    public CurseForgeCatalog(IHttpFetcher http, string apiKey, ILogger<CurseForgeCatalog> logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("CurseForge requires an API key.", nameof(apiKey));
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    public ModCatalogKind Kind => ModCatalogKind.CurseForge;

    // Note: the production IHttpFetcher would need to inject the x-api-key header.
    // For simplicity this implementation assumes the underlying HttpClient has the key
    // configured; if not, callers should use the Modrinth catalog (no key needed).

    public async Task<IReadOnlyList<ModSearchResult>> SearchAsync(
        string query, string? gameVersion = null, ModLoader? loader = null,
        int limit = 20, CancellationToken ct = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["gameId"] = MinecraftGameId.ToString();
        qs["classId"] = ModsClassId.ToString();
        qs["searchFilter"] = query;
        qs["pageSize"] = Math.Min(limit, 50).ToString();
        qs["sortField"] = "2"; // Popularity
        if (gameVersion is not null) qs["gameVersion"] = gameVersion;
        if (loader is not null && loader != ModLoader.Any)
            qs["modLoaderType"] = LoaderInt(loader.Value).ToString();

        string url = $"{BaseUrl}/mods/search?{qs}";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var results = new List<ModSearchResult>();
        foreach (var m in data.EnumerateArray())
        {
            results.Add(new ModSearchResult
            {
                ProjectId = GetInt(m, "id").ToString(),
                Slug = GetString(m, "slug"),
                Title = GetString(m, "name"),
                Description = GetString(m, "summary"),
                Author = m.TryGetProperty("authors", out var a) && a.GetArrayLength() > 0
                    ? GetString(a[0], "name") : string.Empty,
                Downloads = GetLong(m, "downloadCount"),
                Categories = m.TryGetProperty("categories", out var cats)
                    ? cats.EnumerateArray().Select(c => GetString(c, "name")).ToList()
                    : Array.Empty<string>(),
                IconUrl = m.TryGetProperty("logo", out var logo) ? GetString(logo, "thumbnailUrl") : string.Empty,
                Source = ModCatalogKind.CurseForge,
            });
        }
        return results;
    }

    public async Task<ModProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        string url = $"{BaseUrl}/mods/{projectId}";
        string json;
        try { json = await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        using var doc = JsonDocument.Parse(json);
        JsonElement m = doc.RootElement.GetProperty("data");
        return new ModProject
        {
            ProjectId = GetInt(m, "id").ToString(),
            Slug = GetString(m, "slug"),
            Title = GetString(m, "name"),
            Description = GetString(m, "summary"),
            Body = string.Empty, // CurseForge needs a separate /description endpoint
            Source = ModCatalogKind.CurseForge,
        };
    }

    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(
        string projectId, string gameVersion, ModLoader loader, CancellationToken ct = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["gameVersion"] = gameVersion;
        qs["modLoaderType"] = LoaderInt(loader).ToString();

        string url = $"{BaseUrl}/mods/{projectId}/files?{qs}";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var files = new List<ModFile>();
        foreach (var f in data.EnumerateArray())
        {
            files.Add(new ModFile
            {
                FileName = GetString(f, "fileName"),
                DownloadUrl = GetString(f, "downloadUrl"),
                Sha1 = f.TryGetProperty("hashes", out var hashes) &&
                       hashes.EnumerateArray().Any(h =>
                           GetInt(h, "algo") == 1) // 1 = SHA-1
                    ? hashes.EnumerateArray().First(h => GetInt(h, "algo") == 1)
                          .TryGetProperty("value", out var v) ? v.GetString() : null
                    : null,
                Size = GetLong(f, "fileLength"),
                GameVersion = gameVersion,
                Loader = loader,
                Source = ModCatalogKind.CurseForge,
            });
        }
        return files;
    }

    /// <summary>
    /// Batch-resolve modpack file descriptors. The CurseForge modpack <c>manifest.json</c>
    /// lists each mod as <c>{ projectID, fileID }</c>; this endpoint turns those into real
    /// download URLs + metadata. POSTs to <c>/mods/files</c> with the id pairs in the body.
    /// Requires the configured <c>x-api-key</c> (CurseForge policy).
    /// </summary>
    public async Task<IReadOnlyList<CurseForgeModpackFile>> ResolveModpackFilesAsync(
        IReadOnlyList<(int ProjectId, int FileId)> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<CurseForgeModpackFile>();

        // The /mods/files endpoint accepts a JSON body { "fileIds": [ { "modId":.., "fileId":.. } ] }.
        string body = JsonSerializer.Serialize(new
        {
            fileIds = ids.Select(p => new { modId = p.ProjectId, fileId = p.FileId }),
        });

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync($"{BaseUrl}/mods/files", content, ct);
        resp.EnsureSuccessStatusCode();
        string json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var resolved = new List<CurseForgeModpackFile>();
        foreach (var f in data.EnumerateArray())
        {
            int modId = GetInt(f, "modId");
            int fileId = GetInt(f, "id");
            resolved.Add(new CurseForgeModpackFile
            {
                ProjectId = modId,
                FileId = fileId,
                FileName = GetString(f, "fileName"),
                DownloadUrl = GetString(f, "downloadUrl"),
                Sha1 = f.TryGetProperty("hashes", out var hashes) &&
                       hashes.EnumerateArray().Any(h => GetInt(h, "algo") == 1)
                    ? hashes.EnumerateArray().First(h => GetInt(h, "algo") == 1)
                          .TryGetProperty("value", out var v) ? v.GetString() : null
                    : null,
                Size = GetLong(f, "fileLength"),
            });
        }
        return resolved;
    }

    /// <summary>
    /// <see cref="ICurseForgeFileResolver"/> implementation: batch-resolve (projectID, fileID)
    /// pairs into <see cref="CurseForgeResolvedFile"/>s for the modpack installer.
    /// </summary>
    public async Task<IReadOnlyList<CurseForgeResolvedFile>> ResolveAsync(
        IReadOnlyList<(int ProjectId, int FileId)> ids, CancellationToken ct = default)
    {
        IReadOnlyList<CurseForgeModpackFile> resolved = await ResolveModpackFilesAsync(ids, ct);
        return resolved.Select(f => new CurseForgeResolvedFile
        {
            ProjectId = f.ProjectId,
            FileId = f.FileId,
            FileName = f.FileName,
            DownloadUrl = f.DownloadUrl,
            Sha1 = f.Sha1,
            Size = f.Size,
        }).ToList();
    }

    private static int LoaderInt(ModLoader l) => l switch
    {
        ModLoader.Fabric => 4,
        ModLoader.Forge => 1,
        ModLoader.Quilt => 5,
        ModLoader.NeoForge => 6,
        _ => 0,
    };

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;

    private static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64() : 0;
}

/// <summary>A CurseForge modpack file resolved from a (projectID, fileID) pair.</summary>
public sealed class CurseForgeModpackFile
{
    public int ProjectId { get; init; }
    public int FileId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Sha1 { get; init; }
    public long Size { get; init; }
}
