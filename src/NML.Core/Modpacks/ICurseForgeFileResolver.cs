namespace NML.Core.Modpacks;

/// <summary>
/// Resolves CurseForge modpack (projectID, fileID) pairs into real download descriptors.
/// Implemented by NML.Data.CurseForge.CurseForgeCatalog (which holds the x-api-key) and
/// injected into <see cref="ModpackInstaller"/>. Kept as an interface in Core so the
/// modpack installer doesn't pull NML.Data (which already references Core — no cycle).
/// </summary>
public interface ICurseForgeFileResolver
{
    /// <summary>Batch-resolve a set of (projectID, fileID) pairs to download URLs + metadata.</summary>
    Task<IReadOnlyList<CurseForgeResolvedFile>> ResolveAsync(
        IReadOnlyList<(int ProjectId, int FileId)> ids, CancellationToken ct = default);
}

/// <summary>A resolved CurseForge modpack file (download URL + integrity + name).</summary>
public sealed class CurseForgeResolvedFile
{
    public int ProjectId { get; init; }
    public int FileId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Sha1 { get; init; }
    public long Size { get; init; }
}
