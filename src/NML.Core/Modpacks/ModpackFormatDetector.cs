using System.Collections.Generic;
using System.IO.Compression;

namespace NML.Core.Modpacks;

/// <summary>The recognized modpack archive formats.</summary>
public enum ModpackFormat
{
    /// <summary>Modrinth <c>.mrpack</c> (zip with <c>modrinth_index.json</c> + <c>overrides/</c>).</summary>
    Modrinth,
    /// <summary>CurseForge modpack (zip with <c>manifest.json</c> + <c>overrides/</c>).</summary>
    CurseForge,
    /// <summary>An NML deep-export bundle (zip with <c>instance.json</c>) — re-importable as an instance.</summary>
    InstanceBundle,
    /// <summary>Unrecognized archive — neither a known modpack nor an instance bundle.</summary>
    Unknown,
}

/// <summary>
/// Identifies a modpack archive's format from its contents, so the launcher can route the import
/// to the right handler (Modrinth/CurseForge → ModpackInstaller, InstanceBundle → InstanceTransferService)
/// and surface a clear "detected: X" status before committing to the install. Pure + unit-tested
/// against synthetic zip entries.
/// </summary>
public static class ModpackFormatDetector
{
    /// <summary>The zip-root marker files each format is identified by.</summary>
    public const string ModrinthMarker = "modrinth.index.json";
    public const string CurseForgeMarker = "manifest.json";
    public const string InstanceMarker = "instance.json";

    /// <summary>Detect the format from an opened zip archive's entry names.</summary>
    public static ModpackFormat Detect(ZipArchive archive)
    {
        // instance.json takes priority — a CurseForge manifest is also named manifest.json but an
        // NML bundle carries instance.json; a Modrinth pack carries modrinth.index.json. Order the
        // checks so the most-specific marker wins.
        if (HasEntry(archive, InstanceMarker)) return ModpackFormat.InstanceBundle;
        if (HasEntry(archive, ModrinthMarker)) return ModpackFormat.Modrinth;
        if (HasEntry(archive, CurseForgeMarker)) return ModpackFormat.CurseForge;
        return ModpackFormat.Unknown;
    }

    /// <summary>Detect the format from an on-disk zip without the caller opening it. Returns
    /// <see cref="ModpackFormat.Unknown"/> when the file is missing or not a readable zip.</summary>
    public static ModpackFormat DetectFile(string zipPath)
    {
        if (!File.Exists(zipPath)) return ModpackFormat.Unknown;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return Detect(archive);
        }
        catch
        {
            return ModpackFormat.Unknown; // corrupt or not a zip
        }
    }

    /// <summary>True when the archive contains a root-level entry with the given name
    /// (path separators normalized, case-insensitive on Windows-friendly archives).</summary>
    private static bool HasEntry(ZipArchive archive, string name)
    {
        foreach (var e in archive.Entries)
        {
            string entry = e.FullName.Replace('\\', '/').TrimStart('/');
            if (string.Equals(entry, name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
