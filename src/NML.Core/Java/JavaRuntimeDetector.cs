using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace NML.Core.Java;

/// <summary>
/// Detects Java runtimes on the current machine by scanning well-known install
/// locations and (optionally) the system PATH, then probing <c>java -version</c> for
/// the exact major version. Designed to be fully unit-testable via a process runner
/// and a path enumerator injected in.
/// </summary>
public sealed class JavaRuntimeDetector
{
    private readonly ILogger<JavaRuntimeDetector> _logger;

    public JavaRuntimeDetector(ILogger<JavaRuntimeDetector> logger) => _logger = logger;

    /// <summary>
    /// Scan the standard install locations for the current OS and return all detected runtimes.
    /// </summary>
    public List<JavaRuntime> DetectAll(Func<string, (int Major, string Output)?>? probe = null)
    {
        probe ??= ProbeJavaVersion;
        var results = new List<JavaRuntime>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string binDir in CandidateInstallDirs().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(binDir)) continue;
            if (!seen.Add(binDir)) continue;

            string exe = Path.Combine(binDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "javaw.exe" : "java");
            if (!File.Exists(exe))
            {
                // Some distributions expose `java` only — fall back to it.
                exe = Path.Combine(binDir, "java");
                if (!File.Exists(exe)) continue;
            }

            (int Major, string Output)? probed = probe(exe);
            if (probed is null) continue;

            results.Add(new JavaRuntime
            {
                BinDirectory = binDir,
                ExecutablePath = exe,
                MajorVersion = probed.Value.Major,
                IsJavaw = exe.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase)
                          || exe.EndsWith("javaw", StringComparison.OrdinalIgnoreCase),
            });
            _logger.LogDebug("Detected Java {Ver} at {Path}", probed.Value.Major, binDir);
        }

        // Best match first: prefer higher major version.
        return results.OrderByDescending(r => r.MajorVersion).ToList();
    }

    /// <summary>Find the best runtime for a required major version, or null if none qualifies.</summary>
    public JavaRuntime? FindForVersion(int requiredMajor, IEnumerable<JavaRuntime> runtimes) =>
        runtimes.FirstOrDefault(r => r.MajorVersion == requiredMajor)
        ?? runtimes.FirstOrDefault(r => r.MajorVersion > requiredMajor);

    /// <summary>
    /// Probe <c>java -version</c> and parse the major version from its stderr output.
    /// Lines look like: <c>openjdk version "17.0.9" 2023-10-17</c> or <c>java version "1.8.0_362"</c>.
    /// </summary>
    public static (int Major, string Output)? ProbeJavaVersion(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath, "-version")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return (ParseMajorVersion(stderr), stderr);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse the Java major version from <c>java -version</c> output. Handles both the
    /// legacy <c>1.8.0_x</c> style (returns 8) and modern <c>17.0.x</c> style (returns 17).
    /// </summary>
    public static int ParseMajorVersion(string versionOutput)
    {
        // Find the first quoted version string, e.g. version "17.0.9".
        var match = System.Text.RegularExpressions.Regex.Match(
            versionOutput, @"version\s+""(?<v>\d+(?:\.\d+)*)");

        if (!match.Success) return 0;
        string[] parts = match.Groups["v"].Value.Split('.');
        if (parts.Length == 0) return 0;

        // 1.8 → 8; 17.x → 17; etc.
        if (parts[0] == "1" && parts.Length > 1) return int.Parse(parts[1]);
        return int.Parse(parts[0]);
    }

    /// <summary>Well-known Java install locations for the current OS.</summary>
    private static IEnumerable<string> CandidateInstallDirs()
    {
        // 1) The runtime managed by this launcher (highest priority).
        string mcRoot = Environment.GetEnvironmentVariable("NML_MINECRAFT_ROOT") ?? string.Empty;
        if (!string.IsNullOrEmpty(mcRoot))
        {
            foreach (string dir in SafeDirs(Path.Combine(mcRoot, "runtime")))
                yield return dir;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (string dir in SafeDirs(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java")))
                yield return dir;
            foreach (string dir in SafeDirs(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")))
                yield return dir;
            string oracle = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium");
            foreach (string dir in SafeDirs(oracle)) yield return dir;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Library/Java/JavaVirtualMachines/Contents/Home/bin";
            foreach (string dir in SafeDirs("/Library/Java/JavaVirtualMachines"))
                yield return Path.Combine(dir, "Contents", "Home", "bin");
        }
        else // Linux
        {
            foreach (string dir in SafeDirs("/usr/lib/jvm")) yield return Path.Combine(dir, "bin");
            foreach (string dir in SafeDirs("/usr/java")) yield return Path.Combine(dir, "bin");
            foreach (string dir in SafeDirs(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks")))
                yield return Path.Combine(dir, "bin");
        }

        // PATH-based fallback: if `java`/`javaw` is on PATH, include its directory.
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string pathDir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathDir)) continue;
                yield return pathDir.Trim();
            }
        }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
