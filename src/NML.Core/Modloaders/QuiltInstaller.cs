using Microsoft.Extensions.Logging;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Modloaders;

/// <summary>
/// Quilt installer. Quilt's meta API mirrors Fabric's shape (it's a fork) but lives at
/// <c>https://meta.quiltmc.org/v3</c>. We subclass <see cref="FabricInstaller"/> behavior
/// via composition rather than inheritance to keep the endpoint distinct.
/// </summary>
public sealed class QuiltInstaller
{
    private const string MetaApi = "https://meta.quiltmc.org/v3";

    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly ILogger<QuiltInstaller> _logger;

    public QuiltInstaller(
        IHttpFetcher http,
        Downloader downloader,
        ILogger<QuiltInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FabricLoaderInfo>> ListLoadersAsync(
        string gameVersion, CancellationToken ct = default)
    {
        string json = await _http.GetStringAsync($"{MetaApi}/versions/loader/{gameVersion}", ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

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

    public async Task<string> InstallAsync(
        string gameVersion,
        string loaderVersion,
        MinecraftDirectory mc,
        DownloadCancel? cancel = null,
        ProgressReporter? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Installing Quilt {Loader} for {Game}…", loaderVersion, gameVersion);

        string profileId = $"quilt-loader-{loaderVersion}-{gameVersion}";
        string profileUrl = $"{MetaApi}/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
        string profileJson = await _http.GetStringAsync(profileUrl, ct);

        Directory.CreateDirectory(mc.VersionDir(profileId));
        await File.WriteAllTextAsync(mc.VersionJson(profileId), profileJson, ct);

        // Quilt profile JSON uses the same Library/Downloadable shape; delegate to the
        // downloader by parsing with NML.Core.Models.JsonOptions.
        var profile = System.Text.Json.JsonSerializer.Deserialize<Models.VersionInfo>(
            profileJson, Models.JsonOptions.Default);
        if (profile?.Libraries is { Count: > 0 })
        {
            var toFetch = new List<(Downloadable File, string RelativePath)>();
            foreach (Models.Library lib in profile.Libraries)
            {
                if (lib.Downloads?.Artifact is null) continue;
                string rel = lib.Downloads.Artifact.Path ?? lib.Coordinate.RelativePath;
                toFetch.Add((lib.Downloads.Artifact, rel));
            }
            if (toFetch.Count > 0)
            {
                await _downloader.DownloadBatchAsync(toFetch, mc.LibrariesDir, maxConcurrency: 8,
                    cancel, progress, ct);
            }
        }

        _logger.LogInformation("Quilt installed as profile {Id}.", profileId);
        return profileId;
    }
}
