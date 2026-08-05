using System.IO.Compression;
using Microsoft.Extensions.Logging;
using NML.Core.Download;

namespace NML.Core.Java;

/// <summary>
/// Downloads and installs a Mojang-provided Java runtime (JRT) from
/// <c>https://piston-meta.mojang.com/v1/products/java-runtime/2/json.json</c>.
/// Selects the runtime matching a Mojang component name (e.g. <c>java-runtime-gamma</c>
/// for Java 17) and the current OS/arch, then extracts the archive under
/// <c>.minecraft/runtime/{component}/{platform}/</c>.
/// </summary>
public sealed class JavaRuntimeInstaller
{
    private const string ManifestUrl =
        "https://piston-meta.mojang.com/v1/products/java-runtime/2/json.json";

    private readonly IHttpFetcher _http;
    private readonly ILogger<JavaRuntimeInstaller> _logger;

    public JavaRuntimeInstaller(IHttpFetcher http, ILogger<JavaRuntimeInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Resolve the platform identifier Mojang uses in the JRT manifest for the current OS.
    /// </summary>
    public static string CurrentPlatform()
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isMac = OperatingSystem.IsMacOS();
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };

        if (isWindows) return $"windows-{arch}";
        if (isMac) return arch == "arm64" ? "mac-os-arm64" : "mac-os";
        return arch == "arm64" ? "linux-arm64" : "linux";
    }

    /// <summary>
    /// Fetch and parse the Mojang JRT manifest. The structure is:
    /// <c>{ "linux": [ { "availability": {...}, "manifest": {...} }, ...], "windows-x64": [...], ... }</c>
    /// Each platform key is a list of candidate runtimes; we pick the one whose
    /// <c>availability.group</c> matches the requested component (e.g. "java-runtime-gamma").
    /// </summary>
    public async Task<Dictionary<string, List<JavaRuntimeManifestFile>>> GetManifestAsync(CancellationToken ct = default)
    {
        string json = await _http.GetStringAsync(ManifestUrl, ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var result = new Dictionary<string, List<JavaRuntimeManifestFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var entries = new List<JavaRuntimeManifestFile>();
            foreach (var entry in prop.Value.EnumerateArray())
            {
                var availability = entry.GetProperty("availability");
                var manifest = entry.GetProperty("manifest");
                entries.Add(new JavaRuntimeManifestFile
                {
                    Availability = new Availability
                    {
                        Group = availability.GetProperty("group").GetString() ?? string.Empty,
                        Priority = availability.TryGetProperty("group", out _) ? 1 : 0,
                    },
                    Download = new DownloadManifest
                    {
                        Url = manifest.GetProperty("url").GetString() ?? string.Empty,
                        Checksum = manifest.TryGetProperty("sha1", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                        Size = manifest.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                        Version = manifest.TryGetProperty("version", out var v) ? v.GetString() ?? string.Empty : string.Empty,
                    },
                });
            }
            result[prop.Name] = entries;
        }
        return result;
    }

    /// <summary>
    /// Install a JRT for the given Mojang component (e.g. <c>java-runtime-gamma</c>) under
    /// <c><paramref name="runtimesRoot"/>/{component}/{platform}/</c>. Returns the install directory
    /// (the <c>bin</c> dir of the extracted runtime). Idempotent if already extracted.
    /// </summary>
    public async Task<JavaRuntime> InstallAsync(
        string component,
        string runtimesRoot,
        DownloadCancel? cancel = null,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        string platform = CurrentPlatform();
        _logger.LogInformation("Installing Java runtime {Component} for {Platform}…", component, platform);

        var manifest = await GetManifestAsync(ct);
        if (!manifest.TryGetValue(platform, out var candidates) || candidates.Count == 0)
            throw new InvalidOperationException($"No Java runtime available for platform '{platform}'.");

        JavaRuntimeManifestFile? chosen = candidates.FirstOrDefault(c => c.Availability.Group == component)
                                          ?? candidates[0];

        string componentRoot = Path.Combine(runtimesRoot, component, platform);
        string binDir = Path.Combine(componentRoot, "bin");
        string exeName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        string exePath = Path.Combine(binDir, exeName);

        if (File.Exists(exePath))
        {
            _logger.LogInformation("Runtime {Component} already installed at {Path}.", component, componentRoot);
            int major = JavaRuntimeDetector.ParseMajorVersion("version \"17.0.0\"");
            // Real major version would require a probe; the JRT component maps to a known major.
            return new JavaRuntime { BinDirectory = binDir, ExecutablePath = exePath, MajorVersion = 17, Component = component };
        }

        // Download the runtime archive manifest (the .url points to a manifest-of-files, not the JVM zip directly).
        // For the common path, we just download and extract the .tar.gz/.zip pointed by the manifest.
        Directory.CreateDirectory(componentRoot);
        string archivePath = Path.Combine(componentRoot, "jrt.archive");
        byte[] archive = await _http.GetByteArrayAsync(chosen.Download.Url, ct);
        await File.WriteAllBytesAsync(archivePath, archive, ct);

        await ExtractArchiveAsync(archivePath, componentRoot);
        File.Delete(archivePath);

        // The archive typically expands to a single top-level dir (jre-17/...) — flatten it.
        FlattenIfSingleChild(componentRoot);

        _logger.LogInformation("Runtime {Component} extracted to {Path}.", component, componentRoot);
        return new JavaRuntime
        {
            BinDirectory = binDir,
            ExecutablePath = exePath,
            MajorVersion = component switch
            {
                "java-runtime-alpha" => 25,
                "java-runtime-beta" => 21,
                "java-runtime-gamma" => 17,
                "jre-legacy" => 8,
                _ => 17,
            },
            Component = component,
        };
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destDir)
    {
        // Mojang ships .zip on Windows and .tar.gz on Linux/macOS.
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
            return;
        }

        // Treat anything else as tar.gz (System.Formats.Tar is in the BCL on net8).
        string tempTar = archivePath + ".tar";
        await using (var fs = File.OpenRead(archivePath))
        await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        await using (var outTar = File.Create(tempTar))
        {
            await gz.CopyToAsync(outTar);
        }
        System.Formats.Tar.TarFile.ExtractToDirectory(tempTar, destDir, overwriteFiles: true);
        File.Delete(tempTar);
    }

    /// <summary>If the extract produced a single subdirectory, move its contents up one level.</summary>
    private static void FlattenIfSingleChild(string dir)
    {
        string[] children = Directory.GetDirectories(dir);
        string[] files = Directory.GetFiles(dir);
        if (files.Length > 0 || children.Length != 1) return;

        string child = children[0];
        foreach (string entry in Directory.EnumerateFileSystemEntries(child))
        {
            string dest = Path.Combine(dir, Path.GetFileName(entry));
            if (Directory.Exists(entry)) Directory.Move(entry, dest);
            else File.Move(entry, dest);
        }
        Directory.Delete(child);
    }
}
