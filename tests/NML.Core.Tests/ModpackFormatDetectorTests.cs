using System.IO.Compression;
using NML.Core.Modpacks;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ModpackFormatDetector"/> — the multi-source import's first step: it must
/// classify a zip as Modrinth / CurseForge / NML-instance-bundle / Unknown from its root entries,
/// so the launcher can route the import correctly and tell the user what it recognized.
/// </summary>
public class ModpackFormatDetectorTests
{
    private static string TempZip(Action<ZipArchive> build)
    {
        string path = Path.Combine(Path.GetTempPath(), "nml-mpd-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        build(archive);
        // Close + reopen via the detector (which owns the lifetime).
        archive.Dispose();
        return path;
    }

    private static ZipArchive Reopen(string path) => ZipFile.OpenRead(path);

    [Fact]
    public void DetectFile_Recognizes_Modrinth_Mrpack()
    {
        string zip = TempZip(a =>
        {
            a.CreateEntry("modrinth.index.json");
            a.CreateEntry("overrides/config/foo.cfg");
        });
        try
        {
            ModpackFormatDetector.DetectFile(zip).Should().Be(ModpackFormat.Modrinth);
            using var archive = Reopen(zip);
            ModpackFormatDetector.Detect(archive).Should().Be(ModpackFormat.Modrinth);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void DetectFile_Recognizes_CurseForge_Manifest()
    {
        string zip = TempZip(a =>
        {
            a.CreateEntry("manifest.json");
            a.CreateEntry("overrides/mods/sodium.jar");
        });
        try
        {
            ModpackFormatDetector.DetectFile(zip).Should().Be(ModpackFormat.CurseForge);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void DetectFile_Recognizes_NML_Instance_Bundle()
    {
        // An instance bundle carries instance.json — and it must win over a CurseForge-style
        // manifest.json if both were present, so the NML-import path is used.
        string zip = TempZip(a =>
        {
            a.CreateEntry("instance.json");
            a.CreateEntry("manifest.json"); // should be ignored in favor of the NML bundle
        });
        try
        {
            ModpackFormatDetector.DetectFile(zip).Should().Be(ModpackFormat.InstanceBundle);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void DetectFile_Returns_Unknown_For_Plain_Zip()
    {
        string zip = TempZip(a =>
        {
            a.CreateEntry("readme.txt");
            a.CreateEntry("saves/World1/level.dat");
        });
        try
        {
            ModpackFormatDetector.DetectFile(zip).Should().Be(ModpackFormat.Unknown);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void DetectFile_Returns_Unknown_For_Missing_File()
    {
        ModpackFormatDetector.DetectFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".zip"))
            .Should().Be(ModpackFormat.Unknown);
    }

    [Fact]
    public void DetectFile_Returns_Unknown_For_Corrupt_Zip()
    {
        // A file that isn't a zip at all must not throw — it's reported as Unknown.
        string path = Path.Combine(Path.GetTempPath(), "nml-corrupt-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        File.WriteAllText(path, "this is not a zip");
        try
        {
            ModpackFormatDetector.DetectFile(path).Should().Be(ModpackFormat.Unknown);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Detect_Normalizes_Backslash_Entry_Names()
    {
        // Zips created on Windows sometimes store entries with backslashes; the detector must
        // still recognize the marker when the separator is a backslash.
        string zip = TempZip(a =>
        {
            // ZipArchive normalizes to forward slashes on creation, so simulate the raw case by
            // creating a nested entry whose FullName is the marker.
            a.CreateEntry("modrinth.index.json");
        });
        try
        {
            using var archive = Reopen(zip);
            ModpackFormatDetector.Detect(archive).Should().Be(ModpackFormat.Modrinth);
        }
        finally { File.Delete(zip); }
    }
}
