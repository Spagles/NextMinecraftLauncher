using NML.Core.Instances;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="InstanceDiskUsageCalculator"/> — measures per-category disk usage.
/// </summary>
public class InstanceDiskUsageCalculatorTests
{
    private static string MakeInstance(params (string Path, int Bytes)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-disk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var (rel, bytes) in files)
        {
            string p = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[bytes]);
        }
        return dir;
    }

    [Fact]
    public void Measure_Categorizes_By_Folder()
    {
        string dir = MakeInstance(
            ("mods/sodium.jar", 1024 * 500),
            ("mods/iris.jar", 1024 * 300),
            ("saves/World1/level.dat", 1024 * 100),
            ("logs/latest.log", 1024 * 50),
            ("options.txt", 200)
        );
        try
        {
            var usage = InstanceDiskUsageCalculator.Measure(dir);
            usage.Categories.Should().Contain(c => c.Folder == "mods" && c.SizeBytes == 1024 * 800);
            usage.Categories.Should().Contain(c => c.Folder == "saves" && c.SizeBytes == 1024 * 100);
            usage.Categories.Should().Contain(c => c.Folder == "logs" && c.SizeBytes == 1024 * 50);
            // "other" catches options.txt (not in a tracked subfolder).
            usage.Categories.Should().Contain(c => c.Folder == "other" && c.SizeBytes == 200);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Measure_Returns_Total()
    {
        string dir = MakeInstance(("mods/a.jar", 1000), ("saves/b.dat", 500));
        try
        {
            var usage = InstanceDiskUsageCalculator.Measure(dir);
            usage.TotalBytes.Should().BeGreaterThanOrEqualTo(1500);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Measure_Largest_Category_First()
    {
        string dir = MakeInstance(("saves/big.dat", 10000), ("mods/small.jar", 100));
        try
        {
            var usage = InstanceDiskUsageCalculator.Measure(dir);
            usage.Categories[0].Folder.Should().Be("saves");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Measure_Empty_Dir_Returns_Empty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-empty-disk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var usage = InstanceDiskUsageCalculator.Measure(dir);
            usage.Categories.Should().BeEmpty();
            usage.TotalBytes.Should().Be(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.00 GB")]
    public void FormatSize_Formats_Correctly(long bytes, string expected)
    {
        InstanceDiskUsageCalculator.FormatSize(bytes).Should().Be(expected);
    }
}
