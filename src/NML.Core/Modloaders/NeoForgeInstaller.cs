using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Modloaders;

/// <summary>
/// NeoForge installer. NeoForge is a community fork of Forge (post-1.20). Its maven layout
/// lives at <c>maven.neoforged.net/releases/net/neoforged/neoforge</c> and the install profile
/// is an <c>install_profile.json</c> fetched per (game, loader) build — same shape as Forge's.
/// This installer mirrors <see cref="ForgeInstaller"/> but targets the NeoForge maven.
/// </summary>
public sealed class NeoForgeInstaller
{
    // BMCLAPI mirror first (works in CN networks), official maven as fallback.
    private const string MavenBase = "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge";
    private const string MavenFallback = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly VersionInfoService _versions;
    private readonly ILogger<NeoForgeInstaller> _logger;

    public NeoForgeInstaller(
        IHttpFetcher http,
        Downloader downloader,
        VersionInfoService versions,
        ILogger<NeoForgeInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>
    /// List available NeoForge loader versions for a given Minecraft version, newest first.
    /// NeoForge's version format is <c>{mc_version}-{build}</c> (e.g. "20.1.5" for MC 1.20.1).
    /// </summary>
    public async Task<IReadOnlyList<NeoForgeVersion>> ListVersionsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        string metaUrl = $"{MavenBase}/maven-metadata.xml";
        string xml;
        try
        {
            xml = await _http.GetStringAsync(metaUrl, ct);
        }
        catch (Exception ex)
        {
            // The mirror may be unreachable (non-CN networks). Try official maven.
            _logger.LogWarning(ex, "NeoForge BMCLAPI mirror unreachable, trying official maven…");
            try
            {
                string fallbackUrl = $"{MavenFallback}/maven-metadata.xml";
                xml = await _http.GetStringAsync(fallbackUrl, ct);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Both NeoForge BMCLAPI and official maven are unreachable.");
                throw new InvalidOperationException(
                    "NeoForge maven is unreachable. This is likely a network issue (GFW/DNS/firewall). " +
                    "Try using a VPN, or check your network connection. " +
                    "You can still play with Fabric, Quilt, or Forge instead.", ex2);
            }
        }

        // NeoForge uses a short version key derived from the MC version (e.g. 1.20.1 → "20.1").
        // The maven versions look like "20.1.5-beta", "20.1.47", etc.
        string prefix = gameVersion.StartsWith("1.", StringComparison.OrdinalIgnoreCase)
            ? gameVersion[2..]  // strip "1." → "20.1"
            : gameVersion;

        var versions = new List<NeoForgeVersion>();
        foreach (Match m in Regex.Matches(xml, @"<version>(?<v>[^<]+)</version>"))
        {
            string v = m.Groups["v"].Value;
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                versions.Add(new NeoForgeVersion
                {
                    LoaderVersion = v,
                    GameVersion = gameVersion,
                });
            }
        }
        return versions;
    }

    /// <summary>
    /// Install NeoForge for <paramref name="gameVersion"/> using <paramref name="loaderVersion"/>
    /// (the NeoForge build number, e.g. "20.1.47").
    /// </summary>
    public async Task<string> InstallAsync(
        string gameVersion,
        string loaderVersion,
        MinecraftDirectory mc,
        DownloadCancel? cancel = null,
        ProgressReporter? progress = null,
        string? javaExecutableForProcessors = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Installing NeoForge {Loader} for {Game}…", loaderVersion, gameVersion);

        // NeoForge's install_profile.json URL.
        string profileUrl = $"{MavenBase}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
        // The installer jar contains install_profile.json as an entry; alternatively, some builds
        // expose it directly. For the MVP we fetch the installer jar's embedded profile by
        // downloading it and extracting. Simpler path: NeoForge also publishes a version JSON
        // at {MavenBase}/{loaderVersion}/neoforge-{loaderVersion}-client.json (for some builds).
        // We try the client JSON first (the common case), then fall back.

        string profileId = $"neoforge-{loaderVersion}";
        string versionJsonUrl = $"{MavenBase}/{loaderVersion}/neoforge-{loaderVersion}-client.json";

        string? versionJson = null;
        try
        {
            versionJson = await _http.GetStringAsync(versionJsonUrl, ct);
            // Write it as the profile's version.json so the launcher recognizes it.
            Directory.CreateDirectory(mc.VersionDir(profileId));
            await File.WriteAllTextAsync(mc.VersionJson(profileId), versionJson, ct);
        }
        catch (HttpRequestException)
        {
            // Fall back: download the installer jar and extract install_profile.json from it.
            _logger.LogDebug("Direct version JSON not found; falling back to installer jar extraction.");
            versionJson = await ExtractProfileFromInstallerJarAsync(profileUrl, profileId, mc, ct);
        }

        // Download NeoForge libraries listed in the version JSON.
        var profile = JsonSerializer.Deserialize<VersionInfo>(versionJson ?? "{}", JsonOptions.Default)
                      ?? new VersionInfo();
        var toFetch = new List<(Downloadable File, string RelativePath)>();
        if (profile.Libraries is not null)
        {
            foreach (Library lib in profile.Libraries)
            {
                if (lib.Downloads?.Artifact is null) continue;
                string rel = lib.Downloads.Artifact.Path ?? lib.Coordinate.RelativePath;
                toFetch.Add((lib.Downloads.Artifact, rel));
            }
        }

        if (toFetch.Count > 0)
        {
            _logger.LogInformation("Downloading {Count} NeoForge libraries…", toFetch.Count);
            await _downloader.DownloadBatchAsync(toFetch, mc.LibrariesDir, maxConcurrency: 8,
                cancel, progress, ct);
        }

        _logger.LogInformation("NeoForge installed as profile {Id}.", profileId);
        return profileId;
    }

    /// <summary>Download the installer jar and extract its embedded install_profile.json. Returns the JSON string.</summary>
    private async Task<string> ExtractProfileFromInstallerJarAsync(
        string installerUrl, string profileId, MinecraftDirectory mc, CancellationToken ct)
    {
        byte[] jarBytes = await _http.GetByteArrayAsync(installerUrl, ct);
        string tempJar = Path.Combine(Path.GetTempPath(), $"neoforge-installer-{Guid.NewGuid():N}.jar");
        await File.WriteAllBytesAsync(tempJar, jarBytes, ct);

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(tempJar);
            var entry = archive.GetEntry("install_profile.json");
            if (entry is null)
                throw new InvalidDataException("NeoForge installer jar has no install_profile.json.");

            using var s = entry.Open();
            using var reader = new StreamReader(s);
            string json = await reader.ReadToEndAsync(ct);

            Directory.CreateDirectory(mc.VersionDir(profileId));
            await File.WriteAllTextAsync(mc.VersionJson(profileId), json, ct);
            return json;
        }
        finally
        {
            try { File.Delete(tempJar); } catch { /* cleanup best-effort */ }
        }
    }
}

public sealed class NeoForgeVersion
{
    /// <summary>The NeoForge build identifier (e.g. "20.1.47").</summary>
    public string LoaderVersion { get; init; } = string.Empty;

    /// <summary>The Minecraft version it targets (e.g. "1.20.1").</summary>
    public string GameVersion { get; init; } = string.Empty;
}
