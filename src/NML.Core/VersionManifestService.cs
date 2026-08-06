using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core;

/// <summary>
/// Fetches and caches the Mojang <c>version_manifest_v2.json</c>. Provides lookups
/// by version id, lists of releases/snapshots, and resolves the <see cref="VersionInfo"/>
/// for a chosen id (caching the parsed version.json on disk).
/// </summary>
public sealed class VersionManifestService
{
    private const string ManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly IHttpFetcher _http;
    private readonly ILogger<VersionManifestService> _logger;
    private VersionManifest? _cached;

    /// <summary>Optional disk cache (when set, the manifest is persisted + served on startup).</summary>
    public ManifestDiskCache? DiskCache { get; set; }

    public VersionManifestService(IHttpFetcher http, ILogger<VersionManifestService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Fetch (or return cached) the version manifest. Uses the disk cache when available:
    /// serves the cached copy when fresh, re-fetches when stale or forced.</summary>
    public async Task<VersionManifest> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cached is not null) return _cached;

        // Try the disk cache first (avoids a network round-trip on every startup).
        if (!forceRefresh && DiskCache is not null && DiskCache.IsFresh())
        {
            string? cachedJson = DiskCache.Load();
            if (cachedJson is not null)
            {
                try
                {
                    _cached = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(cachedJson, JsonOptions.Default);
                    if (_cached is not null)
                    {
                        _logger.LogInformation("Loaded version manifest from disk cache ({Count} versions).", _cached.Versions.Count);
                        return _cached;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Disk cache corrupt; will re-fetch.");
                    DiskCache.Clear();
                }
            }
        }

        _logger.LogInformation("Fetching Mojang version manifest…");
        string json = await _http.GetStringAsync(ManifestUrl, ct);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions.Default)
                       ?? throw new InvalidDataException("Version manifest deserialized to null.");

        _cached = manifest;

        // Persist to the disk cache for next startup.
        if (DiskCache is not null)
        {
            try { DiskCache.Save(json); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to write manifest disk cache."); }
        }

        _logger.LogInformation("Manifest loaded: {Count} versions, latest release = {Release}.",
            manifest.Versions.Count, manifest.Latest.Release);
        return manifest;
    }

    /// <summary>Find a single manifest entry by id, or null.</summary>
    public async Task<VersionManifestEntry?> FindEntryAsync(string versionId, CancellationToken ct = default)
    {
        VersionManifest m = await GetAsync(ct: ct);
        return m.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>List all versions of a given type (release/snapshot/old_beta/old_alpha).</summary>
    public async Task<IReadOnlyList<VersionManifestEntry>> ListByTypeAsync(string type, CancellationToken ct = default)
    {
        VersionManifest m = await GetAsync(ct: ct);
        return m.Versions
            .Where(v => string.Equals(v.Type, type, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
