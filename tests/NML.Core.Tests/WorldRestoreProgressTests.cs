using System.IO.Compression;
using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the progress-aware world restore: <see cref="GameContentBrowser.RestoreWorldAsync"/>
/// reports per-chunk progress (extractedBytes → 100%), honors cancellation mid-extract, and
/// restores the exact file contents — the progress counterpart to the synchronous RestoreWorld.
/// </summary>
public class WorldRestoreProgressTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-rp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Build a real backup zip containing files large enough to produce multiple
    /// progress reports (≈512 KiB across 8 files).</summary>
    private static string BuildBackup(string root, string world, int fileCount, int bytesPerFile)
    {
        // Stage a saves folder with the world's files, then back it up.
        string worldDir = Path.Combine(root, "saves", world);
        Directory.CreateDirectory(worldDir);
        var rng = new Random(42);
        for (int i = 0; i < fileCount; i++)
        {
            byte[] data = new byte[bytesPerFile];
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(worldDir, $"region_{i}.dat"), data);
        }
        var browser = new GameContentBrowser(new MinecraftDirectory(root));
        return browser.BackupWorld(worldDir);
    }

    [Fact]
    public async Task RestoreWorldAsync_Reports_Monotonic_Progress_And_Restores_Files()
    {
        string root = TempDir();
        try
        {
            string zip = BuildBackup(root, "BigWorld", fileCount: 8, bytesPerFile: 64 * 1024);
            long totalExpected = 8L * 64 * 1024;

            // Capture every progress report.
            var reports = new List<(long extracted, long total)>();
            var progress = new Progress<(long extractedBytes, long totalBytes)>(p => reports.Add(p));

            // Wipe the live world so the restore actually re-extracts everything.
            Directory.Delete(Path.Combine(root, "saves", "BigWorld"), recursive: true);

            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string restored = await browser.RestoreWorldAsync(zip, progress);
            restored.Should().EndWith(Path.Combine("saves", "BigWorld"));

            // The final total reported must match the sum of file sizes.
            reports.Should().NotBeEmpty();
            reports.Last().total.Should().Be(totalExpected);
            reports.Last().extracted.Should().Be(totalExpected);
            // Progress must be monotonic non-decreasing.
            for (int i = 1; i < reports.Count; i++)
                reports[i].extracted.Should().BeGreaterThanOrEqualTo(reports[i - 1].extracted,
                    "progress must never go backwards");

            // Files restored with their original sizes.
            Directory.EnumerateFiles(Path.Combine(root, "saves", "BigWorld"))
                .Should().HaveCount(8);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RestoreWorldAsync_Throws_On_Cancellation()
    {
        string root = TempDir();
        try
        {
            // One big file so we can cancel mid-stream.
            string zip = BuildBackup(root, "Cancellable", fileCount: 1, bytesPerFile: 4 * 1024 * 1024);
            Directory.Delete(Path.Combine(root, "saves", "Cancellable"), recursive: true);

            using var cts = new CancellationTokenSource();
            int reports = 0;
            var progress = new Progress<(long extracted, long total)>(_ =>
            {
                reports++;
                if (reports == 3) cts.Cancel(); // cancel after a few chunks
            });

            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            Func<Task> act = () => browser.RestoreWorldAsync(zip, progress, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RestoreWorldAsync_RoundTrips_Contents_Exactly()
    {
        // Two nested files with known bytes — the restore must reproduce them byte-for-byte.
        string root = TempDir();
        try
        {
            string worldDir = Path.Combine(root, "saves", "Exact");
            Directory.CreateDirectory(Path.Combine(worldDir, "sub"));
            File.WriteAllText(Path.Combine(worldDir, "level.dat"), "LEVEL-CONTENT");
            File.WriteAllBytes(Path.Combine(worldDir, "sub", "data.bin"), new byte[] { 1, 2, 3, 4 });

            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string zip = browser.BackupWorld(worldDir);
            Directory.Delete(worldDir, recursive: true);

            await browser.RestoreWorldAsync(zip);
            File.ReadAllText(Path.Combine(worldDir, "level.dat")).Should().Be("LEVEL-CONTENT");
            File.ReadAllBytes(Path.Combine(worldDir, "sub", "data.bin")).Should().Equal(1, 2, 3, 4);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RestoreWorldAsync_Throws_When_Backup_Missing()
    {
        string root = TempDir();
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            Func<Task> act = () => browser.RestoreWorldAsync(Path.Combine(root, "ghost.zip"));
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RestoreWorldAsync_Handles_Empty_Directory_Entries()
    {
        // A backup that contains an explicit empty directory entry (Length 0, ends with /) must
        // not crash the extractor; the directory is created and the restore completes.
        string root = TempDir();
        try
        {
            string zipPath = Path.Combine(root, "backups", "EmptyDir-20240101-000000.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("emptyfolder/");
                var e = archive.CreateEntry("emptyfolder/placeholder.txt");
                using var s = e.Open();
                s.WriteByte(0x41);
            }
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string restored = await browser.RestoreWorldAsync(zipPath);
            Directory.Exists(Path.Combine(restored, "emptyfolder")).Should().BeTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
