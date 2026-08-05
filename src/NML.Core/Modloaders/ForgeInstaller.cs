using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Download;
using NML.Core.Models;

namespace NML.Core.Modloaders;

/// <summary>
/// Forge installer. Forge publishes an <c>install_profile.json</c> per (game, loader) build
/// which carries a versionInfo (to write as the profile's version.json) plus the libraries
/// Forge needs. This installer:
/// <list type="number">
/// <item>Fetches <c>install_profile.json</c> from the Forge maven.</item>
/// <item>Writes the embedded <c>versionInfo</c> as <c>versions/{profile}/{profile}.json</c>
///   (so the launcher's version resolver picks it up via inheritsFrom).</item>
/// <item>Downloads the Forge + Mlify libraries.</item>
/// </list>
/// Full Forge "processor" execution (jar signing, bin-patching) is out of scope for this
/// MVP — modern Forge versions ship pre-processed universal jars that work without the
/// processor step for the common case.
/// </summary>
public sealed class ForgeInstaller
{
    private const string MavenBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";

    private readonly IHttpFetcher _http;
    private readonly Downloader _downloader;
    private readonly VersionInfoService _versions;
    private readonly ILogger<ForgeInstaller> _logger;

    public ForgeInstaller(
        IHttpFetcher http,
        Downloader downloader,
        VersionInfoService versions,
        ILogger<ForgeInstaller> logger)
    {
        _http = http;
        _downloader = downloader;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>
    /// List available Forge loader versions for a given Minecraft version, newest first.
    /// Parses the maven-metadata.xml; recommended versions are marked with the release tag.
    /// </summary>
    public async Task<IReadOnlyList<ForgeVersion>> ListVersionsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        string metaUrl = $"{MavenBase}/maven-metadata.xml";
        string xml = await _http.GetStringAsync(metaUrl, ct);

        var versions = new List<ForgeVersion>();
        // Lightweight regex parse of maven-metadata.xml (avoid pulling in full XML DOM).
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(xml, @"<version>(?<v>[^<]+)</version>"))
        {
            string v = m.Groups["v"].Value;
            if (v.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase))
            {
                versions.Add(new ForgeVersion
                {
                    LoaderVersion = v,
                    // Strip "<gameVersion>-" prefix to expose the loader-only version.
                    DisplayVersion = v[(gameVersion.Length + 1)..],
                });
            }
        }
        return versions;
    }

    /// <summary>
    /// Install Forge for <paramref name="gameVersion"/> using <paramref name="loaderVersion"/>
    /// (the full maven coordinate, e.g. <c>1.20.1-47.3.0</c>).
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
        _logger.LogInformation("Installing Forge {Loader} for {Game}…", loaderVersion, gameVersion);

        // Forge's install_profile.json URL.
        string installerUrl = $"{MavenBase}/{gameVersion}-{loaderVersion}/forge-{gameVersion}-{loaderVersion}-installer.json";
        string installJson;
        try { installJson = await _http.GetStringAsync(installerUrl, ct); }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Forge installer for {gameVersion}-{loaderVersion} not found at {installerUrl}: {ex.Message}", ex);
        }

        using var doc = JsonDocument.Parse(installJson);
        JsonElement root = doc.RootElement;

        // The profile id is e.g. "forge-47.3.0" — Forge's install_profile carries it.
        string profileId = root.TryGetProperty("version", out var ve) && ve.ValueKind == JsonValueKind.String
            ? ve.GetString() ?? $"forge-{loaderVersion}"
            : $"forge-{loaderVersion}";

        // Write the versionInfo as the profile's version.json so the launcher recognizes it.
        if (root.TryGetProperty("versionInfo", out var versionInfo) ||
            root.TryGetProperty("versionInfo", out versionInfo))
        {
            Directory.CreateDirectory(mc.VersionDir(profileId));
            await File.WriteAllTextAsync(mc.VersionJson(profileId), versionInfo.GetRawText(), ct);
        }
        else
        {
            // Some Forge builds embed the versionInfo differently; fall back to a minimal profile.
            Directory.CreateDirectory(mc.VersionDir(profileId));
            var minimal = new VersionInfo
            {
                Id = profileId,
                Type = "release",
                MainClass = "cpw.mods.modlauncher.Launcher",
                InheritsFrom = gameVersion,
            };
            await File.WriteAllTextAsync(mc.VersionJson(profileId),
                JsonSerializer.Serialize(minimal, JsonOptions.Default), ct);
        }

        // Download the Forge libraries listed in install_profile.json.
        var toFetch = new List<(Downloadable File, string RelativePath)>();
        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            foreach (var lib in libs.EnumerateArray())
            {
                // Each lib has "name" (maven coord) + optional "url" base + "downloads.artifact".
                if (lib.TryGetProperty("downloads", out var dl) &&
                    dl.TryGetProperty("artifact", out var art))
                {
                    string? path = art.TryGetProperty("path", out var p) ? p.GetString() : null;
                    string? url = art.TryGetProperty("url", out var u) ? u.GetString() : null;
                    string? sha1 = art.TryGetProperty("sha1", out var s) ? s.GetString() : null;
                    long size = art.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long sv) ? sv : 0;

                    if (path is null || url is null) continue;
                    toFetch.Add((new Downloadable { Path = path, Url = url, Sha1 = sha1 ?? string.Empty, Size = size }, path));
                }
                else if (lib.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                {
                    // Legacy libs only carry a name + url base; resolve via the maven layout.
                    string name = nameEl.GetString()!;
                    var coord = MavenCoordinate.Parse(name);
                    string? baseUrl = lib.TryGetProperty("url", out var b) ? b.GetString() : null;
                    if (baseUrl is null) continue;
                    string rel = coord.RelativePath;
                    toFetch.Add((new Downloadable
                    {
                        Path = rel,
                        Url = baseUrl.TrimEnd('/') + "/" + rel,
                        Sha1 = string.Empty,
                        Size = 0,
                    }, rel));
                }
            }
        }

        if (toFetch.Count > 0)
        {
            _logger.LogInformation("Downloading {Count} Forge libraries…", toFetch.Count);
            await _downloader.DownloadBatchAsync(toFetch, mc.LibrariesDir, maxConcurrency: 8,
                cancel, progress, ct);
        }

        // If the install profile declares processors (modern Forge), run them. This is what
        // performs deobfuscation, binary patching and jar signing to turn the vanilla client
        // jar into a runnable Forge jar. Requires a Java executable to invoke the tools.
        if (root.TryGetProperty("processors", out var procsEl) &&
            procsEl.ValueKind == JsonValueKind.Array &&
            procsEl.GetArrayLength() > 0)
        {
            if (string.IsNullOrEmpty(javaExecutableForProcessors))
            {
                _logger.LogWarning(
                    "Forge profile has {Count} processors but no javaExecutable was provided; " +
                    "skipping processor execution (the install may be incomplete on old MC versions).",
                    procsEl.GetArrayLength());
            }
            else
            {
                _logger.LogInformation("Forge profile declares {Count} processors; executing…",
                    procsEl.GetArrayLength());

                // Re-parse the install_profile.json into the strongly-typed model.
                var profile = JsonSerializer.Deserialize<Forge.ForgeInstallProfile>(installJson, JsonOptions.Default)
                              ?? new Forge.ForgeInstallProfile();

                string clientJar = mc.VersionJar(gameVersion);
                var procCtx = new Forge.ForgeProcessorContext
                {
                    RootDir = mc.Root,
                    LibraryDir = mc.LibrariesDir,
                    MinecraftJar = clientJar,
                    ProcessorDir = Path.Combine(mc.Root, "data"),
                    InstallerJar = installerUrl, // informational; processors reference libs by coord
                    Side = "client",
                };

                var executor = new Forge.ForgeProcessorExecutor(
                    javaExecutableForProcessors!,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<Forge.ForgeProcessorExecutor>.Instance);
                await executor.ExecuteAsync(profile, procCtx, side: "client", ct);
            }
        }

        _logger.LogInformation("Forge installed as profile {Id}.", profileId);
        return profileId;
    }
}

public sealed class ForgeVersion
{
    /// <summary>Full maven coordinate, e.g. <c>1.20.1-47.3.0</c>.</summary>
    public string LoaderVersion { get; init; } = string.Empty;

    /// <summary>Loader-only version for display, e.g. <c>47.3.0</c>.</summary>
    public string DisplayVersion { get; init; } = string.Empty;
}
