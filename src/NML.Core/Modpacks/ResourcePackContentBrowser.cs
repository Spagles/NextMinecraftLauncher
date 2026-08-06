using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace NML.Core.Modpacks;

/// <summary>
/// Lists the content inside a Minecraft resource-pack .zip so the user can browse what textures /
/// models / sounds the pack overrides before enabling it. Pure + unit-tested.
/// </summary>
public static class ResourcePackContentBrowser
{
    /// <summary>Asset categories the browser groups files into (for a summary view).</summary>
    public static readonly IReadOnlyDictionary<string, string> CategoryMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "textures", "Textures" },
        { "models", "Models" },
        { "sounds", "Sounds" },
        { "shaders", "Shaders" },
        { "lang", "Languages" },
        { "blockstates", "Block States" },
        { "font", "Fonts" },
        { "mcmeta", "Metadata" },
    };

    /// <summary>List every file inside a resource-pack .zip, grouped by category. Returns an empty
    /// list when the zip is missing or unreadable.</summary>
    public static IReadOnlyList<ResourcePackContentCategory> ListContents(string zipPath)
    {
        if (!System.IO.File.Exists(zipPath)) return Array.Empty<ResourcePackContentCategory>();
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var files = new List<(string Path, string Category, long Size)>();

            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))) continue;
                string name = entry.FullName.Replace('\\', '/');
                // Skip metadata files that aren't content.
                if (name == "pack.mcmeta" || name == "pack.png") continue;

                string category = ClassifyEntry(name);
                files.Add((name, category, entry.Length));
            }

            return files
                .GroupBy(f => f.Category)
                .OrderByDescending(g => g.Count()) // largest category first
                .Select(g => new ResourcePackContentCategory(
                    Category: g.Key,
                    FileCount: g.Count(),
                    TotalSizeBytes: g.Sum(f => f.Size),
                    Files: g.Select(f => f.Path).OrderBy(p => p).ToList()))
                .ToList();
        }
        catch
        {
            return Array.Empty<ResourcePackContentCategory>();
        }
    }

    /// <summary>Classify a zip entry path into an asset category (Textures / Models / Sounds / etc.)
    /// or "Other" when it doesn't match a known category.</summary>
    public static string ClassifyEntry(string entryPath)
    {
        // Resource packs store assets under assets/minecraft/{category}/... or assets/{mod}/{category}/...
        // Also handle root-level categories (some packs put them at the zip root).
        foreach (var (dir, label) in CategoryMap)
        {
            string marker1 = $"/{dir}/";
            string marker2 = $"{dir}/";
            if (entryPath.Contains(marker1, System.StringComparison.OrdinalIgnoreCase) ||
                entryPath.StartsWith(marker2, System.StringComparison.OrdinalIgnoreCase))
                return label;
        }
        return "Other";
    }

    /// <summary>Quick summary: total file count + total size + category count. Shown in the preview
    /// header without expanding the full list.</summary>
    public static ResourcePackContentSummary GetSummary(string zipPath)
    {
        var categories = ListContents(zipPath);
        if (categories.Count == 0) return new ResourcePackContentSummary(0, 0, 0);
        return new ResourcePackContentSummary(
            TotalFiles: categories.Sum(c => c.FileCount),
            TotalSizeBytes: categories.Sum(c => c.TotalSizeBytes),
            CategoryCount: categories.Count);
    }
}

/// <summary>A grouped category of files inside a resource pack.</summary>
public sealed record ResourcePackContentCategory(string Category, int FileCount, long TotalSizeBytes, IReadOnlyList<string> Files);

/// <summary>Summary of a resource pack's content (shown without expanding).</summary>
public sealed record ResourcePackContentSummary(int TotalFiles, long TotalSizeBytes, int CategoryCount);
