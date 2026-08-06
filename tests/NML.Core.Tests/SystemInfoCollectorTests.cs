using NML.Core.Platform;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="SystemInfoCollector"/> — collects CPU cores, RAM, OS, and .NET version
/// for display in Settings.
/// </summary>
public class SystemInfoCollectorTests
{
    [Fact]
    public void Collect_Returns_NonNull_Info()
    {
        var info = SystemInfoCollector.Collect();
        info.Should().NotBeNull();
        info.CpuCores.Should().BeGreaterThan(0);
        info.OsDescription.Should().NotBeEmpty();
        info.OsArchitecture.Should().NotBeEmpty();
        info.DotNetVersion.Should().NotBeEmpty();
        info.MachineName.Should().NotBeEmpty();
    }

    [Fact]
    public void Collect_CpuCores_Matches_Environment()
    {
        var info = SystemInfoCollector.Collect();
        info.CpuCores.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void FormatSummary_Includes_Cores_And_Arch()
    {
        var info = new SystemInfo(
            CpuCores: 8,
            TotalRamMb: 16384,
            OsDescription: "Microsoft Windows 10.0.19045",
            OsArchitecture: "X64",
            ProcessArchitecture: "X64",
            DotNetVersion: "8.0.0",
            MachineName: "MYPC");
        var summary = SystemInfoCollector.FormatSummary(info);
        summary.Should().Contain("8 cores");
        summary.Should().Contain("16.0 GB RAM");
        summary.Should().Contain("X64");
    }

    [Fact]
    public void FormatSummary_Handles_Zero_Ram()
    {
        var info = new SystemInfo(4, 0, "Linux", "X64", "X64", "8.0.0", "host");
        var summary = SystemInfoCollector.FormatSummary(info);
        summary.Should().Contain("RAM unknown");
    }
}
