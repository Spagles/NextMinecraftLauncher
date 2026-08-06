using NML.Core.Logging;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="LatestCrashFinder"/> — the input locator behind the one-click AI crash
/// diagnosis. It finds the newest crash-*.txt in crash-reports/ by last-write time and reads the
/// trailing latest.log lines for runtime context, tolerating missing folders/files.
/// </summary>
public class LatestCrashFinderTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-crash-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteCrash(string gameDir, string fileName, string content, DateTime mtime)
    {
        string path = Path.Combine(gameDir, "crash-reports", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, mtime);
    }

    [Fact]
    public void FindNewestCrashReport_Returns_Newest_By_LastWrite()
    {
        string dir = TempDir();
        try
        {
            WriteCrash(dir, "crash-2023-01-01_00.00.00-client.txt", "old", new DateTime(2023, 1, 1));
            WriteCrash(dir, "crash-2024-06-07_12.00.00-client.txt", "new", new DateTime(2024, 6, 7));
            WriteCrash(dir, "crash-2023-12-31_23.59.59-client.txt", "mid", new DateTime(2023, 12, 31));

            string? newest = LatestCrashFinder.FindNewestCrashReport(dir);
            newest.Should().NotBeNull();
            newest.Should().EndWith("crash-2024-06-07_12.00.00-client.txt");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FindNewestCrashReport_Returns_Null_When_No_CrashReports_Dir()
    {
        string dir = TempDir();
        try
        {
            LatestCrashFinder.FindNewestCrashReport(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FindNewestCrashReport_Returns_Null_When_Dir_Empty()
    {
        string dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "crash-reports"));
            LatestCrashFinder.FindNewestCrashReport(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FindNewestCrashReport_Ignores_NonCrash_Txt_Files()
    {
        // Only files matching crash-*.txt are candidates; a stray readme.txt must be ignored.
        string dir = TempDir();
        try
        {
            string crashDir = Path.Combine(dir, "crash-reports");
            Directory.CreateDirectory(crashDir);
            File.WriteAllText(Path.Combine(crashDir, "readme.txt"), "not a crash");
            LatestCrashFinder.FindNewestCrashReport(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadLatestLogTail_Returns_Trailing_Lines()
    {
        string dir = TempDir();
        try
        {
            string logDir = Path.Combine(dir, "logs");
            Directory.CreateDirectory(logDir);
            var lines = Enumerable.Range(1, 100).Select(i => $"line {i}").ToArray();
            File.WriteAllLines(Path.Combine(logDir, "latest.log"), lines);

            string? tail = LatestCrashFinder.ReadLatestLogTail(dir, tailLines: 5);
            tail.Should().NotBeNull();
            tail!.Split('\n').Should().HaveCount(5);
            tail.Should().Contain("line 100"); // the very last line is always in the tail
            tail.Should().Contain("line 96");  // and the 5th-from-last
            tail.Should().NotContain("line 95"); // but not the 6th-from-last
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadLatestLogTail_Returns_Null_When_No_Log()
    {
        string dir = TempDir();
        try
        {
            LatestCrashFinder.ReadLatestLogTail(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Find_Returns_Both_Inputs_When_Present()
    {
        string dir = TempDir();
        try
        {
            WriteCrash(dir, "crash-2024-01-01_00.00.00-client.txt", "CRASH", new DateTime(2024, 1, 1));
            string logDir = Path.Combine(dir, "logs");
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "latest.log"), "log line\n");

            var inputs = LatestCrashFinder.Find(dir);
            inputs.HasAny.Should().BeTrue();
            inputs.CrashReportPath.Should().NotBeNull();
            inputs.LogTail.Should().NotBeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Find_HasAny_False_When_Neither_Present()
    {
        string dir = TempDir();
        try
        {
            LatestCrashFinder.Find(dir).HasAny.Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
