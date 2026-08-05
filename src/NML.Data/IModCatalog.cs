namespace NML.Data;

/// <summary>
/// A unified mod catalog — the abstraction Modrinth and CurseForge both implement so the
/// launcher (and the AI recommender) can treat them interchangeably. This is the seam that
/// keeps mod sources pluggable.
/// </summary>
public interface IModCatalog
{
    /// <summary>Which source this catalog represents.</summary>
    ModCatalogKind Kind { get; }

    /// <summary>
    /// Search mods by free-text query, optionally filtered by game version and loader.
    /// Returns ranked results (relevance/popularity per source).
    /// </summary>
    Task<IReadOnlyList<ModSearchResult>> SearchAsync(
        string query,
        string? gameVersion = null,
        ModLoader? loader = null,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>Fetch the project detail (full description, gallery, categories).</summary>
    Task<ModProject?> GetProjectAsync(string projectId, CancellationToken ct = default);

    /// <summary>Get downloadable files for a project, filtered to a game version + loader.</summary>
    Task<IReadOnlyList<ModFile>> GetFilesAsync(
        string projectId,
        string gameVersion,
        ModLoader loader,
        CancellationToken ct = default);
}

public enum ModCatalogKind { Modrinth, CurseForge }

public enum ModLoader { Fabric, Forge, Quilt, NeoForge, Any }

/// <summary>A single search hit, normalized across sources.</summary>
public sealed class ModSearchResult
{
    public string ProjectId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    /// <summary>Download count (popularity signal for ranking).</summary>
    public long Downloads { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public string IconUrl { get; init; } = string.Empty;
    public ModCatalogKind Source { get; init; }
}

/// <summary>A full project's metadata.</summary>
public sealed class ModProject
{
    public string ProjectId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty; // full readme/desc (markdown or html)
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModLoader> Loaders { get; init; } = Array.Empty<string>().Select(_ => ModLoader.Any).ToList();
    public string IconUrl { get; init; } = string.Empty;
    public ModCatalogKind Source { get; init; }
}

/// <summary>A concrete downloadable file for a mod (one specific version).</summary>
public sealed class ModFile
{
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    /// <summary>SHA-1 (Modrinth) or SHA-1 (CurseForge); used for integrity.</summary>
    public string? Sha1 { get; init; }
    public long Size { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public ModLoader Loader { get; init; } = ModLoader.Any;
    public ModCatalogKind Source { get; init; }
}
