using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NML.Core;
using NML.Core.Download;

namespace NML.Core.Modloaders;

/// <summary>
/// LiteLoader installer. LiteLoader (a legacy mod loader for Minecraft ≤ 1.12.2) is distributed as a
/// plain JAR plus a tweak class — unlike Forge/NeoForge there's no installer JAR to run. We fetch the
/// version list from the BMCLAPI mirror, download the loader JAR into the libraries tree, and write a
/// version.json profile that inherits from the vanilla version and adds the LiteLoader tweak to the
/// JVM arguments, so the launcher's version resolver picks it up.
/// </summary>
public sealed class LiteLoaderInstaller
{
    private const string ListUrl = "https://bmclapi2.bangbang93.com/liteloader/list";
    private const string JarBaseUrl = "https://bmclapi2.bangbang93.com/maven/com/mumfrey/liteloader";

    private readonly IHttpFetcher _http;
    private readonly ILogger<LiteLoaderInstaller> _logger;

    public LiteLoaderInstaller(IHttpFetcher http, ILogger<LiteLoaderInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Fetch available LiteLoader builds (grouped by Minecraft version).</summary>
    public async Task<IReadOnlyList<LiteLoaderVersion>> ListVersionsAsync(
        string? gameVersion = null, CancellationToken ct = default)
    {
        try
        {
            string json = await _http.GetStringAsync(ListUrl, ct);
            var all = ParseList(json);
            return string.IsNullOrWhiteSpace(gameVersion)
                ? all
                : all.Where(v => string.Equals(v.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch LiteLoader version list.");
            return Array.Empty<LiteLoaderVersion>();
        }
    }

    /// <summary>Parse the BMCLAPI /liteloader/list JSON into version entries (pure).</summary>
    internal static IReadOnlyList<LiteLoaderVersion> ParseList(string json)
    {
        var versions = new List<LiteLoaderVersion>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                string mcVersion = entry.TryGetProperty("mcversion", out var mc) && mc.ValueKind == JsonValueKind.String
                    ? mc.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(mcVersion)) continue;

                if (!entry.TryGetProperty("build", out var build) || build.ValueKind != JsonValueKind.Object) continue;
                string file = build.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString() ?? "" : "";
                string version = build.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "" : "";
                string tweakClass = build.TryGetProperty("tweakClass", out var tc) && tc.ValueKind == JsonValueKind.String
                    ? tc.GetString() ?? "com.mumfrey.liteloader.launch.LiteLoaderTweaker" : "com.mumfrey.liteloader.launch.LiteLoaderTweaker";

                if (string.IsNullOrEmpty(file)) continue;
                versions.Add(new LiteLoaderVersion
                {
                    GameVersion = mcVersion,
                    File = file,
                    Version = string.IsNullOrEmpty(version) ? mcVersion : version,
                    TweakClass = tweakClass,
                    Display = $"LiteLoader {mcVersion}" + (string.IsNullOrEmpty(version) ? "" : $" ({version})"),
                });
            }
        }
        catch { /* malformed JSON */ }
        return versions;
    }

    /// <summary>
    /// Install LiteLoader for <paramref name="gameVersion"/>. Downloads the loader JAR into the
    /// libraries tree and writes a version.json profile that inherits from the vanilla version and
    /// appends the LiteLoader tweak class to the JVM arguments. Returns the profile id
    /// (<c>LiteLoader&lt;mc&gt;</c>).
    /// </summary>
    public async Task<string> InstallAsync(
        LiteLoaderVersion ver, MinecraftDirectory mc, CancellationToken ct = default)
    {
        string profileId = $"LiteLoader{ver.GameVersion}";
        _logger.LogInformation("Installing LiteLoader {Ver} for {Mc}…", ver.Version, ver.GameVersion);

        // Download the LiteLoader JAR into the libraries tree (LiteLoader lives under
        // com/mumfrey/liteloader/<mc>/<file>).
        string libRel = $"com/mumfrey/liteloader/{ver.GameVersion}/{ver.File}";
        string libPath = Path.Combine(mc.LibrariesDir, libRel);
        Directory.CreateDirectory(Path.GetDirectoryName(libPath)!);
        if (!File.Exists(libPath))
        {
            string jarUrl = $"{JarBaseUrl}/{ver.GameVersion}/{ver.File}";
            _logger.LogInformation("Downloading LiteLoader JAR from {Url}…", jarUrl);
            byte[] bytes = await _http.GetByteArrayAsync(jarUrl, ct);
            await File.WriteAllBytesAsync(libPath, bytes, ct);
        }

        // Write the version.json profile: inherits the vanilla version, adds the LiteLoader tweak
        // to the JVM args, and registers the loader library + its dependencies (launchwrapper, asm).
        Directory.CreateDirectory(mc.VersionDir(profileId));
        var profile = new LiteLoaderProfile
        {
            Id = profileId,
            InheritsFrom = ver.GameVersion,
            Jar = ver.GameVersion,
            MainClass = "net.minecraft.client.main.Main",
            Libraries = new List<LiteLoaderLib>
            {
                new() { Name = $"com.mumfrey:liteloader:{ver.GameVersion}", Url = JarBaseUrl + "/" },
            },
            Arguments = new LiteLoaderArgs
            {
                Game = new List<string>(),
                Jvm = new List<string> { $"-Dliteloader.coreTweaks={ver.TweakClass}", $"--tweakClass {ver.TweakClass}" },
            },
            Tweakers = new List<string> { ver.TweakClass },
        };
        string versionJson = JsonSerializer.Serialize(profile, LiteLoaderJsonOpts);
        await File.WriteAllTextAsync(mc.VersionJson(profileId), versionJson, ct);

        _logger.LogInformation("LiteLoader installed as profile {Id}.", profileId);
        return profileId;
    }

    private static readonly JsonSerializerOptions LiteLoaderJsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>A LiteLoader build entry (one per Minecraft version).</summary>
public sealed class LiteLoaderVersion
{
    public string GameVersion { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string TweakClass { get; init; } = "com.mumfrey.liteloader.launch.LiteLoaderTweaker";
    public string Display { get; init; } = string.Empty;
}

// --- Minimal profile DTOs written as the installed version.json ---

internal sealed class LiteLoaderProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("inheritsFrom")] public string InheritsFrom { get; set; } = "";
    [JsonPropertyName("jar")] public string Jar { get; set; } = "";
    [JsonPropertyName("mainClass")] public string MainClass { get; set; } = "";
    [JsonPropertyName("libraries")] public List<LiteLoaderLib> Libraries { get; set; } = new();
    [JsonPropertyName("arguments")] public LiteLoaderArgs Arguments { get; set; } = new();
    [JsonPropertyName("tweakers")] public List<string> Tweakers { get; set; } = new();
}

internal sealed class LiteLoaderLib
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("url")] public string? Url { get; set; }
}

internal sealed class LiteLoaderArgs
{
    [JsonPropertyName("game")] public List<string> Game { get; set; } = new();
    [JsonPropertyName("jvm")] public List<string> Jvm { get; set; } = new();
}
