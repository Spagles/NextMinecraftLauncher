using System.IO;
using System.IO.Compression;
using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="GameContentBrowser.ExportScreenshotsToZip"/> — the batch-export helper
/// behind the screenshot grid's "export selected" toolbar button. It must bundle the given PNG
/// paths into a single zip (skipping missing files, de-duping entry-name collisions), used by
/// the launcher to let users share a folder of captures.
/// </summary>
public class ScreenshotExportTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-ss-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePng(string path, byte marker)
    {
        // Minimal 1×1 PNG-ish payload — the exporter doesn't validate content, only bundles bytes.
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, marker });
    }

    [Fact]
    public void Export_Bundles_Selected_Screenshots_Into_Zip()
    {
        string root = TempDir();
        string ssDir = Path.Combine(root, "screenshots");
        Directory.CreateDirectory(ssDir);
        string a = Path.Combine(ssDir, "shot_a.png");
        string b = Path.Combine(ssDir, "shot_b.png");
        WritePng(a, 0xAA);
        WritePng(b, 0xBB);
        string zipPath = Path.Combine(root, "out.zip");

        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            string result = browser.ExportScreenshotsToZip(new[] { a, b }, zipPath);
            result.Should().Be(zipPath);
            File.Exists(zipPath).Should().BeTrue();

            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Select(e => e.Name).Should().BeEquivalentTo(new[] { "shot_a.png", "shot_b.png" });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Export_Skips_Missing_Files_Silently()
    {
        string root = TempDir();
        string ssDir = Path.Combine(root, "screenshots");
        Directory.CreateDirectory(ssDir);
        string a = Path.Combine(ssDir, "real.png");
        WritePng(a, 0xCC);
        string zipPath = Path.Combine(root, "out.zip");

        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            // One real file + one missing path — only the real one lands in the zip.
            browser.ExportScreenshotsToZip(new[] { a, Path.Combine(ssDir, "ghost.png") }, zipPath);
            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Should().ContainSingle().Which.Name.Should().Be("real.png");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Export_DeDupes_Name_Collisions_With_Counter()
    {
        // Two screenshots in different folders but the same filename must both end up in the zip
        // without the second overwriting the first — the helper renames the colliding entry.
        string root = TempDir();
        string dirA = Path.Combine(root, "a");
        string dirB = Path.Combine(root, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        string a = Path.Combine(dirA, "2023-01-01_12.00.00.png");
        string b = Path.Combine(dirB, "2023-01-01_12.00.00.png"); // same filename, different dir
        WritePng(a, 0xAA);
        WritePng(b, 0xBB);
        string zipPath = Path.Combine(root, "out.zip");

        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            browser.ExportScreenshotsToZip(new[] { a, b }, zipPath);
            using var archive = ZipFile.OpenRead(zipPath);
            archive.Entries.Should().HaveCount(2);
            archive.Entries.Select(e => e.Name).Should().Contain("2023-01-01_12.00.00.png");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Export_Creates_Parent_Directory_Of_Output()
    {
        string root = TempDir();
        string ssDir = Path.Combine(root, "screenshots");
        Directory.CreateDirectory(ssDir);
        string a = Path.Combine(ssDir, "x.png");
        WritePng(a, 0xDD);
        // Output path points into a not-yet-existing folder.
        string nested = Path.Combine(root, "nested", "deep", "out.zip");

        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            browser.ExportScreenshotsToZip(new[] { a }, nested);
            File.Exists(nested).Should().BeTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
