using System;
using System.IO;

namespace NML.Core;

/// <summary>
/// Disk-cache layer for the Mojang version manifest: persists the fetched JSON to a local file and
/// serves it on the next startup, re-fetching only when the cache is older than the TTL (default 6h)
/// or when explicitly forced. Pure file operations + TTL math, unit-tested.
/// </summary>
public sealed class ManifestDiskCache
{
    /// <summary>Default cache lifetime: 6 hours. Balances freshness vs. startup latency.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    private readonly string _cacheFile;

    public ManifestDiskCache(string cacheDir)
    {
        Directory.CreateDirectory(cacheDir);
        _cacheFile = Path.Combine(cacheDir, "version_manifest_cache.json");
    }

    /// <summary>The absolute path of the cache file.</summary>
    public string CacheFilePath => _cacheFile;

    /// <summary>True when a cache file exists AND is younger than <paramref name="ttl"/>.</summary>
    public bool IsFresh(TimeSpan? ttl = null)
    {
        if (!File.Exists(_cacheFile)) return false;
        var maxAge = ttl ?? DefaultTtl;
        return File.GetLastWriteTimeUtc(_cacheFile) > DateTime.UtcNow - maxAge;
    }

    /// <summary>Read the cached manifest JSON, or null when no cache exists.</summary>
    public string? Load()
    {
        if (!File.Exists(_cacheFile)) return null;
        try { return File.ReadAllText(_cacheFile); }
        catch { return null; }
    }

    /// <summary>Write the manifest JSON to the cache file (overwrites any prior cache).</summary>
    public void Save(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        File.WriteAllText(_cacheFile, json);
    }

    /// <summary>Delete the cache file (e.g. when the manifest fails to parse).</summary>
    public void Clear()
    {
        if (File.Exists(_cacheFile)) File.Delete(_cacheFile);
    }
}
