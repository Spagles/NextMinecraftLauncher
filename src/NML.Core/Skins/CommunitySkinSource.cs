using System.Text.Json;
using NML.Core.Download;

namespace NML.Core.Skins;

/// <summary>A community-sourced skin entry (a downloadable skin PNG + metadata).</summary>
public sealed class CommunitySkin
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Model { get; init; } = "classic"; // classic | slim
    public string PreviewUrl { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
}

/// <summary>Service for browsing community skins and downloading them as PNG files.</summary>
public interface ICommunitySkinSource
{
    /// <summary>The display name of this source.</summary>
    string Name { get; }

    /// <summary>Browse popular/recent community skins (paginated).</summary>
    Task<IReadOnlyList<CommunitySkin>> BrowseAsync(int page = 0, int pageSize = 24, CancellationToken ct = default);

    /// <summary>Search community skins by name/tag.</summary>
    Task<IReadOnlyList<CommunitySkin>> SearchAsync(string query, int page = 0, int pageSize = 24, CancellationToken ct = default);
}

/// <summary>
/// A community skin source backed by the MineSkin API (<c>api.mineskin.org</c>). MineSkin
/// generates skin textures on demand and exposes a list/search endpoint over its catalog.
/// This implementation uses the public read endpoints (no key required for low-rate use).
/// </summary>
public sealed class MineSkinSource : ICommunitySkinSource
{
    private const string Base = "https://api.mineskin.org";
    private readonly IHttpFetcher _http;

    public MineSkinSource(IHttpFetcher http) => _http = http;

    public string Name => "MineSkin";

    public async Task<IReadOnlyList<CommunitySkin>> BrowseAsync(int page = 0, int pageSize = 24, CancellationToken ct = default)
    {
        // MineSkin's /get/list returns recently-generated skins.
        string url = $"{Base}/get/list?size={Math.Min(pageSize, 60)}&page={page}";
        string json = await _http.GetStringAsync(url, ct);
        return ParseList(json);
    }

    public async Task<IReadOnlyList<CommunitySkin>> SearchAsync(string query, int page = 0, int pageSize = 24, CancellationToken ct = default)
    {
        // MineSkin has no native search; the closest is browsing + client-side filter.
        // For the MVP we fetch a page and filter by name match.
        IReadOnlyList<CommunitySkin> all = await BrowseAsync(page, pageSize, ct);
        if (string.IsNullOrWhiteSpace(query)) return all;
        return all.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || s.Author.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static IReadOnlyList<CommunitySkin> ParseList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            // The list endpoint returns { "skins": [ { "id":.., "name":.., "variant":.., "url":.., "texture":.. } ] }
            if (!doc.RootElement.TryGetProperty("skins", out var skins)) return Array.Empty<CommunitySkin>();

            var list = new List<CommunitySkin>();
            foreach (var s in skins.EnumerateArray())
            {
                string id = s.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() ?? "" : "";
                string name = s.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String
                    ? nEl.GetString() ?? id : id;
                string variant = s.TryGetProperty("variant", out var vEl) && vEl.ValueKind == JsonValueKind.String
                    ? vEl.GetString() ?? "classic" : "classic";
                string? url = s.TryGetProperty("url", out var uEl) && uEl.ValueKind == JsonValueKind.String
                    ? uEl.GetString() : null;

                if (string.IsNullOrEmpty(id)) continue;
                // Build a Crafatar-independent preview using the MineSkin "img" endpoint if available.
                string preview = $"https://mineskin.decks.cf/preview/{id}";
                list.Add(new CommunitySkin
                {
                    Id = id,
                    Name = name,
                    Author = "community",
                    Model = variant,
                    PreviewUrl = preview,
                    DownloadUrl = url ?? $"{Base}/texture/{id}",
                });
            }
            return list;
        }
        catch { return Array.Empty<CommunitySkin>(); }
    }
}
