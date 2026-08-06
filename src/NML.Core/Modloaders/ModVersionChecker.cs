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

            // Try Forge 1.13+: META-INF/mods.toml (the authoritative source for modern Forge).
            // mods.toml is TOML with a [[mods]] table: modId = "examplemod", version = "1.0.0".
            var tomlEntry = zip.GetEntry("META-INF/mods.toml");
            if (tomlEntry is not null)
            {
                using var s = tomlEntry.Open();
                using var reader = new StreamReader(s);
                string toml = reader.ReadToEnd();
                var (tomlId, tomlVer) = ParseModsToml(toml);
                if (!string.IsNullOrEmpty(tomlId))
                {
                    return new InstalledModInfo
                    {
                        FileName = Path.GetFileName(jarPath),
                        ModId = tomlId,
                        // mods.toml's version is often "${file.jarVersion}" (resolved from the manifest
                        // at build time); fall back to the manifest's Implementation-Version when the
                        // TOML value is a placeholder.
                        Version = !string.IsNullOrEmpty(tomlVer) && !tomlVer.Contains("${", StringComparison.Ordinal)
                            ? tomlVer
                            : ParseManifestFromZip(zip, "Implementation-Version"),
                        Loader = "forge",
                    };
                }
            }

            // Try Forge 1.12-: META-INF/MANIFEST.MF
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

    /// <summary>
    /// Extract <c>modId</c> and <c>version</c> from a Forge <c>META-INF/mods.toml</c> body. The
    /// values live in the first <c>[[mods]]</c> table as <c>modId = "..."</c> / <c>version = "..."</c>.
    /// Pure + unit-tested; does not require a full TOML parser (those two fields are all we need).
    /// </summary>
    public static (string modId, string version) ParseModsToml(string toml)
    {
        string modId = string.Empty;
        string version = string.Empty;
        bool inModsTable = false;

        foreach (string raw in toml.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                // Enter the [[mods]] table; any other table header exits it.
                inModsTable = line.Equals("[[mods]]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inModsTable || !line.Contains('=')) continue;

            // key = "value"  →  split on first '=', strip whitespace + quotes.
            int eq = line.IndexOf('=');
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim().Trim('"');
            if (key.Equals("modId", StringComparison.OrdinalIgnoreCase)) modId = value;
            else if (key.Equals("version", StringComparison.OrdinalIgnoreCase)) version = value;
        }
        return (modId, version);
    }

    /// <summary>Read a manifest attribute from a zip's MANIFEST.MF without re-opening the archive
    /// from disk (used by the mods.toml fallback for the ${file.jarVersion} placeholder).</summary>
    private static string ParseManifestFromZip(ZipArchive zip, string key)
    {
        var manifestEntry = zip.GetEntry("META-INF/MANIFEST.MF");
        if (manifestEntry is null) return string.Empty;
        using var s = manifestEntry.Open();
        using var reader = new StreamReader(s);
        return ParseManifestAttribute(reader.ReadToEnd(), key);
    }
}
