namespace NML.Core.Modloaders;

/// <summary>A detected mod conflict (duplicate mod id or mixed loaders).</summary>
public sealed class ModConflict
{
    public string Severity { get; init; } = "warning"; // warning | error
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> AffectedFiles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Detects mod conflicts in an instance's mods/ directory:
/// (1) Duplicate mod ids (same mod installed multiple times — causes crashes).
/// (2) Mixed mod loaders (Fabric + Forge jars coexist — guaranteed crash).
/// </summary>
public static class ModConflictDetector
{
    /// <summary>Scan installed mods and return any conflicts found.</summary>
    public static IReadOnlyList<ModConflict> Detect(IReadOnlyList<InstalledModInfo> mods)
    {
        var conflicts = new List<ModConflict>();

        // 1. Duplicate mod ids — group by ModId, flag any group with >1 entry.
        var byId = mods.Where(m => !string.IsNullOrEmpty(m.ModId))
                       .GroupBy(m => m.ModId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byId)
        {
            var files = group.ToList();
            if (files.Count > 1)
            {
                conflicts.Add(new ModConflict
                {
                    Severity = "error",
                    Description = $"Duplicate mod: '{group.Key}' is installed {files.Count} times " +
                                  $"({string.Join(", ", files.Select(f => f.Version))})",
                    AffectedFiles = files.Select(f => f.FileName).ToList(),
                });
            }
        }

        // 2. Mixed loaders — check if both Fabric and Forge mods are present.
        bool hasFabric = mods.Any(m => m.Loader.Equals("fabric", StringComparison.OrdinalIgnoreCase));
        bool hasForge = mods.Any(m => m.Loader.Equals("forge", StringComparison.OrdinalIgnoreCase));
        if (hasFabric && hasForge)
        {
            conflicts.Add(new ModConflict
            {
                Severity = "error",
                Description = "Mixed loaders detected: Fabric and Forge mods coexist. " +
                              "This will crash the game. Use only one loader per instance.",
                AffectedFiles = mods.Where(m =>
                    m.Loader.Equals("fabric", StringComparison.OrdinalIgnoreCase) ||
                    m.Loader.Equals("forge", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.FileName).ToList(),
            });
        }

        // 3. Quilt + Forge (also incompatible).
        bool hasQuilt = mods.Any(m => m.Loader.Equals("quilt", StringComparison.OrdinalIgnoreCase));
        if (hasQuilt && hasForge)
        {
            conflicts.Add(new ModConflict
            {
                Severity = "error",
                Description = "Mixed loaders: Quilt and Forge mods coexist. " +
                              "This will crash the game.",
                AffectedFiles = mods.Where(m =>
                    m.Loader.Equals("quilt", StringComparison.OrdinalIgnoreCase) ||
                    m.Loader.Equals("forge", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.FileName).ToList(),
            });
        }

        return conflicts;
    }
}
