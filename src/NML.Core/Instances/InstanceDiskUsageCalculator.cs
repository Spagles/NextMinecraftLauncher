using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NML.Core.Instances;

/// <summary>
/// Measures an instance's disk usage by category (mods, saves, resourcepacks, logs, etc.) so the
/// launcher can show which subfolders take the most space — helps users decide what to clean up.
/// Pure file operations + unit-tested.
/// </summary>
public static class InstanceDiskUsageCalculator
{
    /// <summary>The subfolders the calculator categorizes. Others are summed under "Other".</summary>
    public static readonly IReadOnlyList<string> TrackedFolders =
        new[] { "mods", "saves", "config", "resourcepacks", "shaderpacks", "logs", "versions", "assets" };

    /// <summary>Measure disk usage per category under a game dir. Returns per-folder sizes + a total.</summary>
    public static InstanceDiskUsage Measure(string gameDir)
    {
        var categories = new List<DiskUsageCategory>();
        long total = 0;

        foreach (string folder in TrackedFolders)
        {
            string path = Path.Combine(gameDir, folder);
            if (!Directory.Exists(path)) continue;
            long size = DirSize(path);
            categories.Add(new DiskUsageCategory(folder, size));
            total += size;
        }

        // Sum everything else under the game dir.
        long otherSize = 0;
        foreach (string dir in Directory.EnumerateDirectories(gameDir))
        {
            string name = Path.GetFileName(dir);
            if (TrackedFolders.Contains(name, System.StringComparer.OrdinalIgnoreCase)) continue;
            otherSize += DirSize(dir);
        }
        // Add top-level files (options.txt, etc.).
        foreach (string file in Directory.EnumerateFiles(gameDir))
        {
            try { otherSize += new FileInfo(file).Length; } catch { }
        }

        if (otherSize > 0)
        {
            categories.Add(new DiskUsageCategory("other", otherSize));
            total += otherSize;
        }

        return new InstanceDiskUsage(categories.OrderByDescending(c => c.SizeBytes).ToList(), total);
    }

    /// <summary>Recursively sum file sizes in a directory.</summary>
    private static long DirSize(string dir)
    {
        long size = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { /* permission errors etc. */ }
        return size;
    }

    /// <summary>Format a byte count as a human-readable size.</summary>
    public static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        };
    }
}

/// <summary>One category's disk usage.</summary>
public sealed record DiskUsageCategory(string Folder, long SizeBytes)
{
    /// <summary>Human-readable size (e.g. "1.2 GB").</summary>
    public string SizeDisplay => InstanceDiskUsageCalculator.FormatSize(SizeBytes);
}

/// <summary>The full disk-usage breakdown for an instance.</summary>
public sealed record InstanceDiskUsage(IReadOnlyList<DiskUsageCategory> Categories, long TotalBytes)
{
    /// <summary>Human-readable total size.</summary>
    public string TotalDisplay => InstanceDiskUsageCalculator.FormatSize(TotalBytes);
}
