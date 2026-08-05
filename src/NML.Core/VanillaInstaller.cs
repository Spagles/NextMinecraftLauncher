using System.IO.Compression;
using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Models;
using NML.Core.Rules;

namespace NML.Core;

/// <summary>
/// Installs a vanilla Minecraft version end-to-end: client.jar, libraries (filtered
/// by current OS/arch rules), the asset index + asset objects, and extracts native
/// libraries into <c>bin/natives</c>. Idempotent — re-running skips already-valid files.
/// </summary>
public sealed class VanillaInstaller
{
    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly VersionInfoService _versions;
    private readonly ILogger<VanillaInstaller> _logger;

    public VanillaInstaller(
        IHttpFetcher http,
        Downloader downloader,
        VersionInfoService versions,
        ILogger<VanillaInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>
    /// Ensure <paramref name="versionId"/> is fully installed under <paramref name="mc"/>.
    /// Returns the merged <see cref="VersionInfo"/> ready for launching.
    /// </summary>
    public async Task<VersionInfo> InstallAsync(
        string versionId,
        MinecraftDirectory mc,
        RuleContext? ruleCtx = null,
        DownloadCancel? cancel = null,
        ProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        ruleCtx ??= RuleContext.Current();
        _logger.LogInformation("Installing vanilla {Id}…", versionId);

        VersionInfo info = await _versions.GetAsync(versionId, mc, ct);

        await DownloadClientJarAsync(info, mc, cancel, progress, ct);
        await DownloadLibrariesAsync(info, mc, ruleCtx, cancel, progress, ct);
        await DownloadAssetsAsync(info, mc, cancel, progress, ct);
        await ExtractNativesAsync(info, mc, ruleCtx, ct);

        _logger.LogInformation("Install of {Id} complete.", versionId);
        return info;
    }

    private async Task DownloadClientJarAsync(
        VersionInfo info, MinecraftDirectory mc,
        DownloadCancel? cancel, ProgressReporter? progress, CancellationToken ct)
    {
        Downloadable? client = info.Downloads?.Client;
        if (client is null)
            throw new InvalidOperationException(
                $"Version '{info.Id}' has no client download (server-only?).");

        Directory.CreateDirectory(mc.VersionDir(info.Id));
        await _downloader.DownloadAsync(
            client, info.Id + ".jar", mc.VersionDir(info.Id),
            cancel, null, ct);
    }

    private async Task DownloadLibrariesAsync(
        VersionInfo info, MinecraftDirectory mc, RuleContext ruleCtx,
        DownloadCancel? cancel, ProgressReporter? progress, CancellationToken ct)
    {
        var toFetch = new List<(Downloadable File, string RelativePath)>();

        foreach (Library lib in info.Libraries)
        {
            // Skip libraries gated to other OSes/archs.
            if (!RuleEvaluator.IsAllowed(lib.Rules, ruleCtx)) continue;

            // Main artifact (always fetched for current platform).
            if (lib.Downloads?.Artifact is not null)
            {
                string rel = lib.Downloads.Artifact.Path ?? lib.Coordinate.RelativePath;
                toFetch.Add((lib.Downloads.Artifact, rel));
            }

            // Native classifier for the current OS, if any.
            if (lib.Natives is not null && lib.Downloads?.Classifiers is not null)
            {
                if (lib.Natives.TryGetValue(ruleCtx.OsName, out string? classifierKey) &&
                    lib.Downloads.Classifiers.TryGetValue(classifierKey, out Downloadable? native))
                {
                    string rel = native.Path ?? $"{lib.Coordinate.RelativePath}-{classifierKey}";
                    toFetch.Add((native, rel));
                }
            }
        }

        if (toFetch.Count == 0) return;
        _logger.LogInformation("Downloading {Count} libraries…", toFetch.Count);
        await _downloader.DownloadBatchAsync(toFetch, mc.LibrariesDir, maxConcurrency: 8,
            cancel, progress, ct);
    }

    private async Task DownloadAssetsAsync(
        VersionInfo info, MinecraftDirectory mc,
        DownloadCancel? cancel, ProgressReporter? progress, CancellationToken ct)
    {
        AssetIndexRef? indexRef = info.AssetIndex;
        if (indexRef is null)
        {
            _logger.LogWarning("Version {Id} has no asset index reference; skipping assets.", info.Id);
            return;
        }

        // Ensure the index JSON is on disk and current.
        string indexPath = mc.AssetIndexPath(indexRef.Id);
        if (!File.Exists(indexPath))
        {
            Directory.CreateDirectory(mc.AssetIndexesDir);
            string indexJson = await _http.GetStringAsync(indexRef.Url, ct);
            await File.WriteAllTextAsync(indexPath, indexJson, ct);
        }

        // Parse the index to enumerate asset objects.
        string json = await File.ReadAllTextAsync(indexPath, ct);
        AssetIndex? index = System.Text.Json.JsonSerializer.Deserialize<AssetIndex>(json, JsonOptions.Default);
        if (index?.Objects is null || index.Objects.Count == 0) return;

        var toFetch = new List<(Downloadable File, string RelativePath)>(index.Objects.Count);
        foreach ((_, AssetObject obj) in index.Objects)
        {
            // Assets are stored under assets/objects/{hash[0..2]}/{hash} but fetched from
            // resources.download.minecraft.net keyed by the same hash path.
            string rel = Path.Combine(obj.Hash[..2], obj.Hash);
            toFetch.Add((new Downloadable
            {
                Sha1 = obj.Hash,
                Size = obj.Size,
                Url = $"https://resources.download.minecraft.net/{obj.Hash[..2]}/{obj.Hash}",
                Path = rel,
            }, rel));
        }

        _logger.LogInformation("Downloading {Count} asset objects…", toFetch.Count);
        await _downloader.DownloadBatchAsync(toFetch, mc.AssetObjectsDir, maxConcurrency: 16,
            cancel, progress, ct);
    }

    /// <summary>
    /// Extract native JARs (the platform-specific classifier for each lib with a
    /// <c>natives</c> map) into <c>bin/natives</c>, honoring <c>extract.exclude</c>.
    /// </summary>
    private Task ExtractNativesAsync(
        VersionInfo info, MinecraftDirectory mc, RuleContext ruleCtx, CancellationToken ct)
    {
        Directory.CreateDirectory(mc.NativesDir);

        foreach (Library lib in info.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules, ruleCtx)) continue;
            if (lib.Natives is null || lib.Downloads?.Classifiers is null) continue;
            if (!lib.Natives.TryGetValue(ruleCtx.OsName, out string? classifierKey)) continue;
            if (!lib.Downloads.Classifiers.TryGetValue(classifierKey, out Downloadable? native)) continue;

            string rel = native.Path ?? $"{lib.Coordinate.RelativePath}-{classifierKey}";
            string jarPath = mc.LibraryPath(rel);
            if (!File.Exists(jarPath))
            {
                _logger.LogWarning("Native jar missing on disk, skipping extract: {Path}", jarPath);
                continue;
            }

            HashSet<string>? exclude = lib.Extract?.Exclude?.ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var archive = ZipFile.OpenRead(jarPath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (exclude is not null && IsExcluded(entry.FullName, exclude)) continue;

                string dest = Path.Combine(mc.NativesDir, entry.FullName);
                string? dir = Path.GetDirectoryName(dest);
                if (dir is not null) Directory.CreateDirectory(dir);

                entry.ExtractToFile(dest, overwrite: true);
            }

            _logger.LogDebug("Extracted natives from {Jar}.", jarPath);
        }

        return Task.CompletedTask;
    }

    private static bool IsExcluded(string path, HashSet<string> exclude)
    {
        // Mojang's exclude patterns are globs like "META-INF/*". Simple prefix/dir match.
        foreach (string pattern in exclude)
        {
            if (pattern.EndsWith('*'))
            {
                string prefix = pattern[..^1];
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
