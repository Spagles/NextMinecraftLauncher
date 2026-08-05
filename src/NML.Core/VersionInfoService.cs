using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core;

/// <summary>
/// Loads and caches individual <see cref="VersionInfo"/> documents. Caches the JSON
/// on disk under <c>versions/{id}/{id}.json</c> so subsequent launches don't re-fetch.
/// Handles <c>inheritsFrom</c> chains by merging parent + child version metadata.
/// </summary>
public sealed class VersionInfoService
{
    private readonly IHttpFetcher _http;
    private readonly ILogger<VersionInfoService> _logger;
    private readonly VersionManifestService _manifest;

    public VersionInfoService(
        IHttpFetcher http,
        VersionManifestService manifest,
        ILogger<VersionInfoService> logger)
    {
        _http = http;
        _manifest = manifest;
        _logger = logger;
    }

    /// <summary>
    /// Resolve the <see cref="VersionInfo"/> for <paramref name="versionId"/>, downloading
    /// its version.json from the manifest if not present locally. Resolves inheritsFrom.
    /// </summary>
    public async Task<VersionInfo> GetAsync(
        string versionId,
        MinecraftDirectory mc,
        CancellationToken ct = default)
    {
        VersionInfo info = await LoadOrDownloadAsync(versionId, mc, ct);

        // Resolve inheritance (e.g. Forge/Fabric child version inherits a parent).
        if (!string.IsNullOrEmpty(info.InheritsFrom))
        {
            VersionInfo parent = await GetAsync(info.InheritsFrom, mc, ct);
            info = Merge(parent, info);
        }
        return info;
    }

    /// <summary>Load the cached version.json if it exists and is valid, else download it.</summary>
    public async Task<VersionInfo> LoadOrDownloadAsync(
        string versionId, MinecraftDirectory mc, CancellationToken ct = default)
    {
        string localJson = mc.VersionJson(versionId);
        string? json = File.Exists(localJson)
            ? await File.ReadAllTextAsync(localJson, ct)
            : null;

        if (json is null)
        {
            VersionManifestEntry? entry = await _manifest.FindEntryAsync(versionId, ct)
                ?? throw new InvalidOperationException(
                    $"Version '{versionId}' not found in manifest and no local json exists.");

            _logger.LogInformation("Downloading version metadata for {Id}…", versionId);
            json = await _http.GetStringAsync(entry.Url, ct);
            Directory.CreateDirectory(mc.VersionDir(versionId));
            await File.WriteAllTextAsync(localJson, json, ct);
        }

        return JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions.Default)
               ?? throw new InvalidDataException($"Version.json for '{versionId}' deserialized to null.");
    }

    /// <summary>
    /// Merge a parent and child <see cref="VersionInfo"/> (Mojang's inheritsFrom semantics):
    /// child's non-null fields win; libraries and arguments are concatenated (parent first).
    /// </summary>
    public static VersionInfo Merge(VersionInfo parent, VersionInfo child) => new()
    {
        Id = child.Id,
        Type = string.IsNullOrEmpty(child.Type) ? parent.Type : child.Type,
        MainClass = child.MainClass ?? parent.MainClass,
        Assets = child.Assets ?? parent.Assets,
        AssetIndex = child.AssetIndex ?? parent.AssetIndex,
        Downloads = child.Downloads ?? parent.Downloads,
        // Child libraries appended after parent (child may override by name).
        Libraries = parent.Libraries.Concat(child.Libraries)
                       .DistinctBy(l => l.Name)
                       .ToList(),
        Arguments = MergeArguments(parent.Arguments, child.Arguments),
        MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
        JavaVersion = child.JavaVersion ?? parent.JavaVersion,
        Logging = child.Logging ?? parent.Logging,
        ReleaseTime = child.ReleaseTime == default ? parent.ReleaseTime : child.ReleaseTime,
        Time = child.Time == default ? parent.Time : child.Time,
        InheritsFrom = null,
        ComplianceLevel = child.ComplianceLevel,
    };

    private static Arguments? MergeArguments(Arguments? parent, Arguments? child)
    {
        if (parent is null) return child;
        if (child is null) return parent;
        return new Arguments
        {
            Game = parent.Game.Concat(child.Game).ToList(),
            Jvm = parent.Jvm.Concat(child.Jvm).ToList(),
        };
    }
}
