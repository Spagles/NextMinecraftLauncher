using System.Runtime.InteropServices;

namespace NML.Core.Rules;

/// <summary>
/// Resolves the current platform into the normalized identifiers Mojang uses in
/// version.json rules: OS name (<c>windows</c>/<c>linux</c>/<c>osx</c>), arch
/// (<c>x86</c>/<c>x86_64</c>/<c>arm64</c>), and an OS version string (mainly Windows).
/// </summary>
public sealed class RuleContext
{
    public string OsName { get; init; } = string.Empty;
    public string Arch { get; init; } = string.Empty;

    /// <summary>OS version string used by Windows range/regex rules (e.g. "10.0").</summary>
    public string? OsVersion { get; init; }

    /// <summary>Optional feature flags toggled by launch-time choices (demo, resolution...).</summary>
    public IReadOnlyDictionary<string, bool> Features { get; init; }
        = new Dictionary<string, bool>();

    /// <summary>Build a <see cref="RuleContext"/> for the machine this process runs on.</summary>
    public static RuleContext Current()
    {
        var osName = OperatingSystem.IsWindows() ? "windows"
                   : OperatingSystem.IsMacOS() ? "osx"
                   : "linux";

        // Normalize the architecture to Mojang's vocabulary.
        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x86_64",
            Architecture.Arm => "arm32",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        };

        string? osVersion = null;
        if (OperatingSystem.IsWindows())
        {
            osVersion = Environment.OSVersion.Version.ToString();
        }

        return new RuleContext
        {
            OsName = osName,
            Arch = arch,
            OsVersion = osVersion,
        };
    }
}
