using System.IO.Compression;
using System.Text.Json;

namespace NML.Core.Modloaders;

/// <summary>A dependency check result for a single mod.</summary>
public sealed class ModDependencyIssue
{
    public string ModId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    /// <summary>"missing_dependency" or "incompatible_break".</summary>
    public string IssueType { get; init; } = string.Empty;
    /// <summary>The id of the missing/conflicting dependency.</summary>
    public string DependencyId { get; init; } = string.Empty;
    /// <summary>Required version constraint (e.g. ">=1.0.0"), or empty.</summary>
    public string VersionConstraint { get; init; } = string.Empty;
}

/// <summary>
/// Checks Fabric/Quilt mod dependencies by reading fabric.mod.json's "depends" and "breaks"
/// fields from each JAR. Detects: (1) missing required dependencies, (2) declared breaks
/// (hard incompatibilities that are present). Forge mods don't have structured dependency
/// metadata in the JAR, so they're skipped gracefully.
/// </summary>
public static class ModDependencyChecker
{
    /// <summary>
    /// Scan mods and check each one's dependencies against the installed set.
    /// Returns issues found (missing deps, breaks conflicts).
    /// </summary>
    public static IReadOnlyList<ModDependencyIssue> Check(IReadOnlyList<InstalledModInfo> installedMods, string modsDir)
    {
        var issues = new List<ModDependencyIssue>();
        var installedIds = new HashSet<string>(
            installedMods.Where(m => !string.IsNullOrEmpty(m.ModId)).Select(m => m.ModId),
            StringComparer.OrdinalIgnoreCase);

        // Re-read the raw fabric.mod.json for dependency info (the InstalledModInfo doesn't
        // carry depends/breaks). We open each JAR once.
        foreach (var mod in installedMods)
        {
            if (!string.Equals(mod.Loader, "fabric", StringComparison.OrdinalIgnoreCase)) continue;

            string jarPath = Path.Combine(modsDir, mod.FileName);
            if (!File.Exists(jarPath)) continue;

            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                var entry = zip.GetEntry("fabric.mod.json");
                if (entry is null) continue;

                using var s = entry.Open();
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;

                // Check "depends" — each key is a required mod id.
                if (root.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in depends.EnumerateObject())
                    {
                        // Skip common built-in dependencies that are always present.
                        if (dep.Name.Equals("minecraft", StringComparison.OrdinalIgnoreCase) ||
                            dep.Name.Equals("java", StringComparison.OrdinalIgnoreCase) ||
                            dep.Name.Equals("fabricloader", StringComparison.OrdinalIgnoreCase)) continue;

                        if (!installedIds.Contains(dep.Name))
                        {
                            issues.Add(new ModDependencyIssue
                            {
                                ModId = mod.ModId,
                                FileName = mod.FileName,
                                IssueType = "missing_dependency",
                                DependencyId = dep.Name,
                                VersionConstraint = dep.Value.ValueKind == JsonValueKind.String
                                    ? dep.Value.GetString() ?? "" : "",
                            });
                        }
                    }
                }

                // Check "breaks" — each key is a mod that must NOT be present.
                if (root.TryGetProperty("breaks", out var breaks) && breaks.ValueKind == JsonValueKind.Object)
                {
                    foreach (var brk in breaks.EnumerateObject())
                    {
                        if (installedIds.Contains(brk.Name))
                        {
                            issues.Add(new ModDependencyIssue
                            {
                                ModId = mod.ModId,
                                FileName = mod.FileName,
                                IssueType = "incompatible_break",
                                DependencyId = brk.Name,
                                VersionConstraint = "",
                            });
                        }
                    }
                }
            }
            catch { /* skip unreadable jars */ }
        }

        return issues;
    }
}
