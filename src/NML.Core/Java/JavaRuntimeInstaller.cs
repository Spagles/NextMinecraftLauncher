using System.IO.Compression;
using Microsoft.Extensions.Logging;
using NML.Core.Download;

namespace NML.Core.Java;

/// <summary>
/// Downloads and installs a Java runtime (JDK) from the Eclipse Adoptium (AdoptOpenJDK) API.
/// Replaces the dead Mojang JRT manifest endpoint (which returns 404 as of 2026).
/// Fetches the latest LTS JDK for a given major version (17 or 21) matching the current OS/arch.
/// </summary>
public sealed class JavaRuntimeInstaller
{
    private const string AdoptiumLatestUrl =
        "https://api.adoptium.net/v3/assets/latest/{0}/hotspot?architecture={1}&image_type=jdk&os={2}";

    private readonly IHttpFetcher _http;
    private readonly ILogger<JavaRuntimeInstaller> _logger;

    public JavaRuntimeInstaller(IHttpFetcher http, ILogger<JavaRuntimeInstaller> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Resolve the current OS/arch for the Adoptium API.</summary>
    public static string CurrentPlatform()
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isMac = OperatingSystem.IsMacOS();
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x32",
            _ => "x64",
        };
        if (isWindows) return $"windows-{arch}";
        if (isMac) return $"mac-{arch}";
        return $"linux-{arch}";
    }

    /// <summary>
    /// Install a JDK for the given major version (e.g. 17) under
    /// <c><paramref name="runtimesRoot"/>/jdk-{major}/</c>. Returns the install directory.
    /// </summary>
    public async Task<JavaRuntime> InstallAsync(
        string component,
        string runtimesRoot,
        DownloadCancel? cancel = null,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        // Map component → major version. "java-runtime-gamma" = 17, "java-runtime-delta" = 21.
        int majorVersion = component switch
        {
            "java-runtime-alpha" => 8,
            "java-runtime-gamma" => 17,
            "java-runtime-delta" => 21,
            _ when int.TryParse(component, out int v) => v,
            _ => 17,
        };

        string osStr = OperatingSystem.IsWindows() ? "windows"
                      : OperatingSystem.IsMacOS() ? "mac"
                      : "linux";
        string archStr = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            _ => "x64",
        };

        string apiUrl = string.Format(AdoptiumLatestUrl, majorVersion, archStr, osStr);
        _logger.LogInformation("Fetching Adoptium JDK {Major} for {Os}-{Arch}…", majorVersion, osStr, archStr);

        // Fetch the asset metadata JSON.
        string json = await _http.GetStringAsync(apiUrl, ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement.EnumerateArray().First();
        string downloadUrl = root.GetProperty("binary").GetProperty("package").GetProperty("link").GetString()!;
        string pkgName = root.GetProperty("binary").GetProperty("package").GetProperty("name").GetString()!;
        long pkgSize = root.GetProperty("binary").GetProperty("package").GetProperty("size").GetInt64();

        // Determine install dir.
        string installDir = Path.Combine(runtimesRoot, $"jdk-{majorVersion}");
        string binDir = Path.Combine(installDir, "bin");
        string exeName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        string exePath = Path.Combine(binDir, exeName);

        if (File.Exists(exePath))
        {
            _logger.LogInformation("JDK {Major} already installed at {Path}.", majorVersion, installDir);
            return new JavaRuntime { BinDirectory = binDir, ExecutablePath = exePath, MajorVersion = majorVersion, Component = component };
        }

        // Download the archive.
        Directory.CreateDirectory(runtimesRoot);
        string archivePath = Path.Combine(runtimesRoot, pkgName);
        _logger.LogInformation("Downloading {Name} ({Size} bytes)…", pkgName, pkgSize);
        byte[] archive = await _http.GetByteArrayAsync(downloadUrl, ct);
        await File.WriteAllBytesAsync(archivePath, archive, ct);

        // Extract (zip on Windows/mac, tar.gz on Linux).
        Directory.CreateDirectory(installDir);
        if (pkgName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, installDir, overwriteFiles: true);
        }
        else if (pkgName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            // Use tar to extract on Linux/mac.
            var psi = new System.Diagnostics.ProcessStartInfo("tar", $"xzf \"{archivePath}\" -C \"{installDir}\" --strip-components=1")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
        }

        // The archive may extract into a subfolder (e.g. jdk-17.0.9+9/). Find the bin dir.
        if (!Directory.Exists(binDir))
        {
            // Look for a single subdirectory containing bin/.
            foreach (string sub in Directory.GetDirectories(installDir))
            {
                string subBin = Path.Combine(sub, "bin");
                if (Directory.Exists(subBin))
                {
                    // Move contents up.
                    foreach (string entry in Directory.GetFileSystemEntries(sub))
                    {
                        string dest = Path.Combine(installDir, Path.GetFileName(entry));
                        if (!Directory.Exists(dest) && !File.Exists(dest))
                            Directory.Move(entry, dest);
                    }
                    break;
                }
            }
        }

        // Clean up archive.
        try { File.Delete(archivePath); } catch { }

        if (!File.Exists(exePath))
        {
            // Search recursively for javaw.exe / java.
            exePath = Directory.GetFiles(installDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new InvalidOperationException($"Java executable not found after extraction in {installDir}.");
            binDir = Path.GetDirectoryName(exePath)!;
        }

        _logger.LogInformation("JDK {Major} installed at {Path}.", majorVersion, binDir);
        return new JavaRuntime { BinDirectory = binDir, ExecutablePath = exePath, MajorVersion = majorVersion, Component = component };
    }

    /// <summary>Resolve the platform identifier (kept for compatibility with DI consumers).</summary>
    public Task<Dictionary<string, List<JavaRuntimeManifestFile>>> GetManifestAsync(CancellationToken ct = default)
    {
        // Legacy API — Mojang JRT is dead. Return empty so callers know to use InstallAsync directly.
        return Task.FromResult(new Dictionary<string, List<JavaRuntimeManifestFile>>());
    }

    // Legacy model types kept for compatibility.
    public sealed class JavaRuntimeManifestFile
    {
        public Availability Availability { get; set; } = new();
        public DownloadManifest Download { get; set; } = new();
    }
    public sealed class Availability { public string Group { get; set; } = ""; public int Priority { get; set; } }
    public sealed class DownloadManifest { public string Url { get; set; } = ""; public string Checksum { get; set; } = ""; public long Size { get; set; } public string Version { get; set; } = ""; }
}
