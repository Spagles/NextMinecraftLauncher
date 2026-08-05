using System.IO.Compression;
using System.Text.Json;

namespace NML.Core.Modloaders;

/// <summary>A mod's identity extracted from its JAR (id + version + loader type).</summary>
public sealed class InstalledModInfo
{
    public string FileName { get; init; } = string.Empty;
    public string ModId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Loader { get; init; } = "unknown"; // fabric, forge, quilt, etc.
    public bool UpdateAvailable { get; set; }
    public string? LatestVersion { get; set; }
    public string? LatestFileUrl { get; set; }
}

/// <summary>
/// Scans an instance's mods/ directory, extracts mod id + version from each JAR (via
/// fabric.mod.json for Fabric/Quilt, MANIFEST.MF for Forge), and optionally checks each
/// against Modrinth for available updates.
/// </summary>
public static class ModVersionChecker
{
    /// <summary>Scan all .jar files under <paramref name="modsDir"/> and extract mod info.</summary>
    public static IReadOnlyList<InstalledModInfo> ScanInstalledMods(string modsDir)
    {
        var results = new List<InstalledModInfo>();
        if (!Directory.Exists(modsDir)) return results;

        foreach (string jar in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            // Skip disabled mods (.jar.disabled — already filtered by *.jar pattern, but double-check).
            if (jar.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            var info = ExtractModInfo(jar);
            if (info is not null) results.Add(info);
        }
        return results;
    }

    /// <summary>Extract mod id + version from a JAR by reading fabric.mod.json or MANIFEST.MF.</summary>
    public static InstalledModInfo? ExtractModInfo(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            // Try Fabric/Quilt: fabric.mod.json
            var fabricEntry = zip.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var s = fabricEntry.Open();
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                return new InstalledModInfo
                {
                    FileName = Path.GetFileName(jarPath),
                    ModId = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                    Loader = root.TryGetProperty("schemaVersion", out _) ? "fabric" : "fabric",
                };
            }

            // Try Forge: META-INF/MANIFEST.MF
            var manifestEntry = zip.GetEntry("META-INF/MANIFEST.MF");
            if (manifestEntry is not null)
            {
                using var s = manifestEntry.Open();
                using var reader = new StreamReader(s);
                string manifest = reader.ReadToEnd();
                string modId = ParseManifestAttribute(manifest, "Implementation-Title");
                string version = ParseManifestAttribute(manifest, "Implementation-Version");
                if (!string.IsNullOrEmpty(modId))
                {
                    return new InstalledModInfo
                    {
                        FileName = Path.GetFileName(jarPath),
                        ModId = modId,
                        Version = version,
                        Loader = "forge",
                    };
                }
            }

            // Fallback: filename only
            return new InstalledModInfo
            {
                FileName = Path.GetFileName(jarPath),
                ModId = Path.GetFileNameWithoutExtension(jarPath),
                Version = "",
                Loader = "unknown",
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extract a value from a MANIFEST.MF "Key: Value" line.</summary>
    public static string ParseManifestAttribute(string manifest, string key)
    {
        foreach (string line in manifest.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                return trimmed[(key.Length + 1)..].Trim();
        }
        return string.Empty;
    }
}
