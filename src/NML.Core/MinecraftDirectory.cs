namespace NML.Core;

/// <summary>
/// Resolves the layout of a Minecraft working directory (<c>.minecraft</c>).
/// Centralizes path computation so installers and the launcher agree on disk layout.
/// </summary>
public sealed class MinecraftDirectory
{
    /// <summary>Root <c>.minecraft</c> path.</summary>
    public string Root { get; }
    public MinecraftDirectory(string root) => Root = root;

    public string VersionsDir => Path.Combine(Root, "versions");
    public string LibrariesDir => Path.Combine(Root, "libraries");
    public string AssetsDir => Path.Combine(Root, "assets");
    public string AssetObjectsDir => Path.Combine(AssetsDir, "objects");
    public string AssetIndexesDir => Path.Combine(AssetsDir, "indexes");
    public string NativesDir => Path.Combine(Root, "bin", "natives");
    public string RuntimesDir => Path.Combine(Root, "runtime");

    /// <summary>Per-version directory: <c>versions/{id}/</c>.</summary>
    public string VersionDir(string versionId) => Path.Combine(VersionsDir, versionId);

    /// <summary>Per-version jar: <c>versions/{id}/{id}.jar</c>.</summary>
    public string VersionJar(string versionId) =>
        Path.Combine(VersionDir(versionId), versionId + ".jar");

    /// <summary>Per-version metadata: <c>versions/{id}/{id}.json</c>.</summary>
    public string VersionJson(string versionId) =>
        Path.Combine(VersionDir(versionId), versionId + ".json");

    /// <summary>Asset object path: <c>assets/objects/{hash[0..2]}/{hash}</c>.</summary>
    public string AssetObjectPath(string hash) =>
        Path.Combine(AssetObjectsDir, hash[..2], hash);

    /// <summary>Asset index file: <c>assets/indexes/{id}.json</c>.</summary>
    public string AssetIndexPath(string indexId) =>
        Path.Combine(AssetIndexesDir, indexId + ".json");

    public string LibraryPath(string relativePath) =>
        Path.Combine(LibrariesDir, relativePath);
}
