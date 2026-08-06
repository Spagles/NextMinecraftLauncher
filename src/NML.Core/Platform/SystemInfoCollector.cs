using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NML.Core.Platform;

/// <summary>
/// Collects system information (CPU cores, total RAM, OS, .NET runtime version, architecture) for
/// display in the Settings page and for diagnostic/reporting purposes. Pure + unit-tested.
/// </summary>
public static class SystemInfoCollector
{
    /// <summary>Collect all system info into a structured record.</summary>
    public static SystemInfo Collect()
    {
        long ramMb = 0;
        try { ramMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); }
        catch { /* some platforms may not report */ }

        return new SystemInfo(
            CpuCores: Environment.ProcessorCount,
            TotalRamMb: ramMb,
            OsDescription: RuntimeInformation.OSDescription,
            OsArchitecture: RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            DotNetVersion: Environment.Version.ToString(),
            MachineName: Environment.MachineName);
    }

    /// <summary>Format the system info as a human-readable summary string.</summary>
    public static string FormatSummary(SystemInfo info)
    {
        var parts = new List<string>
        {
            $"{info.CpuCores} cores",
            info.TotalRamMb > 0 ? $"{info.TotalRamMb / 1024.0:F1} GB RAM" : "RAM unknown",
            info.OsArchitecture,
        };
        return string.Join(" · ", parts);
    }
}

/// <summary>Immutable snapshot of the system's hardware + OS info.</summary>
public sealed record SystemInfo(
    int CpuCores,
    long TotalRamMb,
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    string DotNetVersion,
    string MachineName);
