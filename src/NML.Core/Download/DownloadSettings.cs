using System.Collections.Generic;

namespace NML.Core.Download;

/// <summary>
/// User-tunable download controls, mirroring PCL's download settings: how many files to fetch
/// at once and which mirror to pull Mojang assets from. The concurrency value is validated
/// (clamped to 1–64) and the mirror URL normalized, so the same settings object is safe to feed
/// straight into <c>DownloadBatchAsync</c> and the URL rewriter.
/// </summary>
public sealed class DownloadSettings
{
    /// <summary>Hard floor for concurrency — a single connection (for slow/unstable links).</summary>
    public const int MinConcurrency = 1;
    /// <summary>Hard ceiling for concurrency — beyond this, servers throttle or drop connections.</summary>
    public const int MaxConcurrency = 64;
    /// <summary>Sensible default matching the launcher's prior hardcoded value.</summary>
    public const int DefaultConcurrency = 8;

    private int _concurrency = DefaultConcurrency;

    /// <summary>Number of simultaneous downloads (1–64). Clamped on set, so the value is always valid.</summary>
    public int Concurrency
    {
        get => _concurrency;
        set => _concurrency = Clamp(value);
    }

    /// <summary>Mirror base URL (no trailing slash), or empty/null = official Mojang endpoints.
    /// Example: <c>https://bmclapi2.bangbang93.com</c>.</summary>
    public string? MirrorUrl { get; set; }

    /// <summary>True when a mirror is configured (drives whether URLs get rewritten).</summary>
    public bool HasMirror => !string.IsNullOrWhiteSpace(MirrorUrl);

    /// <summary>Clamp a candidate concurrency into the allowed range.</summary>
    public static int Clamp(int value) => Math.Max(MinConcurrency, Math.Min(MaxConcurrency, value));
}

/// <summary>
/// Rewrites official Mojang download URLs to route through a configured mirror (BMCLAPI-style),
/// so users behind the GFW or on slow links to Mojang's CDNs can pull assets from a local mirror.
/// Pure and allocation-light; unit-tested in isolation.
/// <para>
/// The remap replaces the known Mojang asset/library/version hosts with the mirror prefix while
/// preserving the path, e.g. <c>https://libraries.minecraft.net/com/foo/bar.jar</c> →
/// <c>https://bmclapi2.bangbang93.com/com/foo/bar.jar</c>.
/// </para>
/// </summary>
public static class MirrorUrlRewriter
{
    /// <summary>The Mojang hosts that serve downloadable assets (libraries, version jars, asset objects,
    /// the version manifest + asset-index documents). Order matters only for readability.</summary>
    public static readonly IReadOnlyList<string> MojangHosts = new[]
    {
        "piston-meta.mojang.com",
        "piston-data.mojang.com",
        "launchermeta.mojang.com",
        "libraries.minecraft.net",
        "assets.minecraft.net",
        "resources.download.minecraft.net",
    };

    /// <summary>
    /// Rewrite <paramref name="url"/> to go through <paramref name="mirrorBaseUrl"/> if it points at
    /// a known Mojang host; otherwise return it unchanged. A null/empty/whitespace mirror disables
    /// rewriting (passthrough), matching the "no mirror" default.
    /// </summary>
    public static string Rewrite(string url, string? mirrorBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(mirrorBaseUrl))
            return url;

        // Normalise the mirror base: trim trailing slash(es).
        string mirror = mirrorBaseUrl!.TrimEnd('/');

        // Match "https://host/..." (or http://), capturing the host + path.
        foreach (string host in MojangHosts)
        {
            string prefixHttps = $"https://{host}/";
            string prefixHttp = $"http://{host}/";
            if (url.StartsWith(prefixHttps, StringComparison.OrdinalIgnoreCase))
                return $"{mirror}/{url[prefixHttps.Length..]}";
            if (url.StartsWith(prefixHttp, StringComparison.OrdinalIgnoreCase))
                return $"{mirror}/{url[prefixHttp.Length..]}";
        }
        return url;
    }

    /// <summary>Rewrite many URLs in one pass (convenience for batch downloads).</summary>
    public static IEnumerable<string> RewriteAll(IEnumerable<string> urls, string? mirrorBaseUrl)
        => urls.Select(u => Rewrite(u, mirrorBaseUrl));
}
