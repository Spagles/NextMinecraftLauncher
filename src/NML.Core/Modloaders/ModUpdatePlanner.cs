using System.Collections.Generic;
using System.Linq;

namespace NML.Core.Modloaders;

/// <summary>
/// Builds a download plan for "upgrade all" from a scan of installed mods: selects every mod flagged
/// as having an update whose <see cref="InstalledModInfo.LatestFileUrl"/> is known, pairs each with
/// the target on-disk path it should replace. Pure + unit-tested; the VM runs the plan (download +
/// replace) against the live mods dir.
/// <para>
/// Defensive: skips mods with no update, no URL, or a URL that doesn't look like a mod file, so the
/// one-click upgrade can't replace a jar with an HTML error page or delete a current mod.
/// </para>
/// </summary>
public static class ModUpdatePlanner
{
    /// <summary>Build the upgrade plan. <paramref name="modsDir"/> is the instance's mods/ folder;
    /// each entry's target path is <c>{modsDir}/{LatestFileName or original FileName}</c>.</summary>
    public static IReadOnlyList<ModUpdateItem> Plan(IEnumerable<InstalledModInfo> installed, string modsDir)
    {
        var plan = new List<ModUpdateItem>();
        foreach (var mod in installed)
        {
            if (!mod.UpdateAvailable) continue;                 // only updatable mods
            if (string.IsNullOrWhiteSpace(mod.LatestFileUrl)) continue; // need a download URL
            if (!IsModFileUrl(mod.LatestFileUrl)) continue;     // guard against HTML/error pages

            string fileName = !string.IsNullOrWhiteSpace(mod.LatestVersion) && LooksLikeFileName(mod.LatestVersion)
                ? mod.LatestVersion!
                : mod.FileName; // fall back to the original filename when LatestVersion isn't a file name
            plan.Add(new ModUpdateItem(
                ModId: mod.ModId,
                SourceUrl: mod.LatestFileUrl!,
                TargetPath: System.IO.Path.Combine(modsDir, fileName),
                OldFileName: mod.FileName));
        }
        return plan;
    }

    /// <summary>True when the URL's path ends with a mod-archive extension.</summary>
    public static bool IsModFileUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        // Strip any query string before checking the extension.
        int q = url!.IndexOf('?');
        string path = q >= 0 ? url[..q] : url;
        return path.EndsWith(".jar", System.StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Heuristic: does <paramref name="value"/> look like a file name (has an extension)
    /// rather than a free-form version string like "1.2.3"?</summary>
    public static bool LooksLikeFileName(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value!.EndsWith(".jar", System.StringComparison.OrdinalIgnoreCase)
               || value.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase));
}

/// <summary>One row of an upgrade plan: the mod to upgrade, where to fetch the newer jar, and where
/// to write it (replacing the old file).</summary>
public sealed record ModUpdateItem(string ModId, string SourceUrl, string TargetPath, string OldFileName);
