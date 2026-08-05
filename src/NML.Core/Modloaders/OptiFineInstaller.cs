using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Download;

namespace NML.Core.Modloaders;

/// <summary>
/// OptiFine installer. OptiFine doesn't have a maven repository or JSON API — its versions
/// are listed on optifine.net/download and the installer JARs follow a fixed URL pattern.
/// This installer fetches the version list from the OptiFine downloads page, lets the user
/// pick a version, downloads the installer JAR, and runs it with Java (the installer patches
/// the vanilla jar + writes a version.json with inheritsFrom).
/// </summary>
public sealed class OptiFineInstaller
{
    private const string DownloadsPage = "https://optifine.net/download";
    private const string InstallerBaseUrl = "https://optifine.net/downloadx";

    private readonly IHttpFetcher _http;
    private readonly ILogger<OptiFineInstaller> _logger;

    public OptiFineInstaller(IHttpFetcher http, ILogger<OptiFineInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Fetch available OptiFine versions for a given Minecraft version. Parses the OptiFine
    /// downloads page HTML (or a community mirror's JSON) for version entries matching the
    /// game version.
    /// </summary>
    public async Task<IReadOnlyList<OptiFineVersion>> ListVersionsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        // OptiFine has a simple JSON-like API at optifine.net/changelog?f=OptiFine_{mc}_{type}
        // But the most reliable approach is the community-maintained BMCLAPI mirror which
        // exposes optifine versions as JSON. We use that.
        const string BmclApi = "https://bmclapi2.bangbang93.com/optifine/{mc}";
        string url = BmclApi.Replace("{mc}", gameVersion);

        try
        {
            string json = await _http.GetStringAsync(url, ct);
            return ParseBmclVersions(json, gameVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch OptiFine versions for {Mc}.", gameVersion);
            return Array.Empty<OptiFineVersion>();
        }
    }

    /// <summary>Parse BMCLAPI's OptiFine version list JSON.</summary>
    internal static IReadOnlyList<OptiFineVersion> ParseBmclVersions(string json, string gameVersion)
    {
        var versions = new List<OptiFineVersion>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                string type = entry.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                string patch = entry.TryGetProperty("patch", out var p) ? p.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(type)) continue;

                versions.Add(new OptiFineVersion
                {
                    GameVersion = gameVersion,
                    Type = type,
                    Patch = patch,
                    Display = $"OptiFine {gameVersion} HD U {type}" + (string.IsNullOrEmpty(patch) ? "" : $" {patch}"),
                });
            }
        }
        catch { /* malformed JSON */ }
        return versions;
    }

    /// <summary>
    /// Install OptiFine for <paramref name="gameVersion"/> using <paramref name="type"/> (e.g. "C6").
    /// Downloads the installer JAR and runs it with Java to patch the vanilla jar.
    /// </summary>
    public async Task<string> InstallAsync(
        string gameVersion,
        string type,
        string patch,
        string installerCacheDir,
        string javaExecutable,
        MinecraftDirectory mc,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Installing OptiFine {Type} for {Game}…", type, gameVersion);

        // Build the profile id: OptiFine uses "OptiFine_{mc}_{type}" as the version id.
        string profileId = $"OptiFine_{gameVersion}_{type}";

        // Download the installer JAR from OptiFine's download endpoint.
        string installerUrl = $"{InstallerBaseUrl}?f=OptiFine_{gameVersion}_HD_U_{type}.jar";
        Directory.CreateDirectory(installerCacheDir);
        string installerJar = Path.Combine(installerCacheDir, $"OptiFine_{gameVersion}_{type}.jar");

        if (!File.Exists(installerJar))
        {
            _logger.LogInformation("Downloading OptiFine installer from {Url}…", installerUrl);
            byte[] bytes = await _http.GetByteArrayAsync(installerUrl, ct);
            await File.WriteAllBytesAsync(installerJar, bytes, ct);
        }

        // Run the installer with Java. The OptiFine installer accepts command-line args:
        //   java -jar OptiFine.jar --install.path={mc.root}
        var psi = new System.Diagnostics.ProcessStartInfo(javaExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = installerCacheDir,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJar);
        psi.ArgumentList.Add($"--install.path={mc.Root}");

        _logger.LogInformation("Running OptiFine installer…");
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) throw new InvalidOperationException("Failed to start Java for OptiFine installer.");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogError("OptiFine installer exited with code {Code}: {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"OptiFine installer failed (exit {process.ExitCode}): {stderr}");
        }

        _logger.LogInformation("OptiFine installed as profile {Id}.", profileId);
        return profileId;
    }
}

public sealed class OptiFineVersion
{
    public string GameVersion { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Patch { get; init; } = string.Empty;
    public string Display { get; init; } = string.Empty;
}
