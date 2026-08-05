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

    public VersionManifestService(IHttpFetcher http, ILogger<VersionManifestService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Fetch (or return cached) the version manifest.</summary>
    public async Task<VersionManifest> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cached is not null) return _cached;

        _logger.LogInformation("Fetching Mojang version manifest…");
        string json = await _http.GetStringAsync(ManifestUrl, ct);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions.Default)
                       ?? throw new InvalidDataException("Version manifest deserialized to null.");

        _cached = manifest;
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
