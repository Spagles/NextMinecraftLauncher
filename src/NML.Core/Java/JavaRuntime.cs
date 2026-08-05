namespace NML.Core.Java;

/// <summary>
/// Represents a discovered Java runtime on disk: its install path and the parsed
/// major version (8, 17, 21, …). Produced by <see cref="JavaRuntimeDetector"/>.
/// </summary>
public sealed class JavaRuntime
{
    /// <summary>Absolute path to the directory containing java/javaw (the <c>bin</c> dir).</summary>
    public string BinDirectory { get; init; } = string.Empty;

    /// <summary>Full path to the executable (java on *nix, javaw.exe on Windows).</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Java major version (8, 11, 17, 21, 25, …).</summary>
    public int MajorVersion { get; init; }

    /// <summary>The Mojang runtime component this is compatible with, if known (e.g. "java-runtime-gamma").</summary>
    public string? Component { get; init; }

    public bool IsJavaw { get; init; }

    public override string ToString() => $"Java {MajorVersion} @ {BinDirectory}";
}

/// <summary>
/// Mojang's runtime manifest entry (one of the files under the JRT manifest URL).
/// Lists one or more availability records, each with OS/arch and a download descriptor.
/// </summary>
public sealed class JavaRuntimeManifest
{
    public IReadOnlyList<JavaRuntimeManifestFile> Files { get; init; } = new List<JavaRuntimeManifestFile>();
}

public sealed class JavaRuntimeManifestFile
{
    /// <summary>OS/arch this availability record targets (e.g. "windows-x64", "linux").</summary>
    public Availability Availability { get; init; } = new();

    public DownloadManifest Download { get; init; } = new();
}

public sealed class Availability
{
    public string Group { get; init; } = string.Empty;
    public int Priority { get; init; }
}

public sealed class DownloadManifest
{
    public string Checksum { get; init; } = string.Empty;

    /// <summary>The .tar.gz / .zip URL of the runtime archive.</summary>
    public string Url { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public long Size { get; init; }
}
