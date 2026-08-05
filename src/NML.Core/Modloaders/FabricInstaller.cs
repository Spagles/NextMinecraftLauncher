using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Modloaders;

/// <summary>
/// Fabric mod loader installer. Fabric exposes a simple metadata API:
/// <c>https://meta.fabricmc.net/v2/versions/loader/{game_version}</c> returns a list of
/// loader+intermediary pairs; choosing the stable one gives two libraries to add plus a
/// profile JSON that we merge into the version's inheritsFrom chain.
/// </summary>
public sealed class FabricInstaller
{
    private const string MetaApi = "https://meta.fabricmc.net/v2";

    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly VersionInfoService _versions;
    private readonly ILogger<FabricInstaller> _logger;

    public FabricInstaller(
        IHttpFetcher http,
        Downloader downloader,
        VersionInfoService versions,
        ILogger<FabricInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>Fetch the available Fabric loaders for a game version, stable first.</summary>
    public async Task<IReadOnlyList<FabricLoaderInfo>> ListLoadersAsync(
        string gameVersion, CancellationToken ct = default)
    {
        string json = await _http.GetStringAsync($"{MetaApi}/versions/loader/{gameVersion}", ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<FabricLoaderInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var loader = item.GetProperty("loader");
            list.Add(new FabricLoaderInfo
            {
                LoaderVersion = loader.GetProperty("version").GetString() ?? string.Empty,
                IsStable = loader.GetProperty("stable").GetBoolean(),
                IntermediaryVersion = item.GetProperty("intermediary").GetProperty("version").GetString() ?? string.Empty,
            });
        }
        return list;
    }

    /// <summary>
    /// Install Fabric for <paramref name="gameVersion"/> using the (stable) <paramref name="loaderVersion"/>.
    /// Creates a profile version <c>fabric-loader-{loader}-{game}</c> that inherits the vanilla version.
    /// </summary>
    public async Task<string> InstallAsync(
        string gameVersion,
        string loaderVersion,
        MinecraftDirectory mc,
        DownloadCancel? cancel = null,
        ProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Installing Fabric {Loader} for {Game}…", loaderVersion, gameVersion);

        // The profile JSON (the version.json that inherits vanilla) is fetched directly.
        string profileId = $"fabric-loader-{loaderVersion}-{gameVersion}";
        string profileUrl = $"{MetaApi}/v2/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
        string profileJson = await _http.GetStringAsync(profileUrl, ct);

        // Parse it to learn which libraries Fabric needs to download.
        var profile = JsonSerializer.Deserialize<VersionInfo>(profileJson, JsonOptions.Default)
                      ?? throw new InvalidDataException("Fabric profile JSON deserialized to null.");

        Directory.CreateDirectory(mc.VersionDir(profileId));
        await File.WriteAllTextAsync(mc.VersionJson(profileId), profileJson, ct);

        // Download the Fabric libraries (intermediary + loader).
        var toFetch = new List<(Downloadable File, string RelativePath)>();
        foreach (Library lib in profile.Libraries)
        {
            if (lib.Downloads?.Artifact is null) continue;
            string rel = lib.Downloads.Artifact.Path ?? lib.Coordinate.RelativePath;
            toFetch.Add((lib.Downloads.Artifact, rel));
        }

        if (toFetch.Count > 0)
        {
            _logger.LogInformation("Downloading {Count} Fabric libraries…", toFetch.Count);
            await _downloader.DownloadBatchAsync(toFetch, mc.LibrariesDir, maxConcurrency: 8,
                cancel, progress, ct);
        }

        _logger.LogInformation("Fabric installed as profile {Id}.", profileId);
        return profileId;
    }
}

public sealed class FabricLoaderInfo
{
    public string LoaderVersion { get; init; } = string.Empty;
    public string IntermediaryVersion { get; init; } = string.Empty;
    public bool IsStable { get; init; }
}
