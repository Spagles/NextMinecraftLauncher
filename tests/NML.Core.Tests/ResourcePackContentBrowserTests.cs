using System.IO.Compression;
using NML.Core.Modpacks;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ResourcePackContentBrowser"/> — lists the files inside a resource-pack .zip
/// grouped by category (Textures / Models / Sounds / etc.) and produces a summary, so the user can
/// preview what a pack overrides before enabling it.
/// </summary>
public class ResourcePackContentBrowserTests
{
    private static string MakePack(params (string Path, int Size)[] entries)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "rpcb-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, size) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var s = entry.Open();
            s.Write(new byte[size], 0, size);
        }
        return zipPath;
    }

    [Fact]
    public void ListContents_Groups_By_Category()
    {
        string zip = MakePack(
            ("assets/minecraft/textures/block/stone.png", 1024),
            ("assets/minecraft/textures/item/diamond.png", 512),
            ("assets/minecraft/models/block/stone.json", 64),
            ("assets/minecraft/sounds/custom.ogg", 2048),
            ("pack.mcmeta", 42),
            ("pack.png", 100)
        );
        try
        {
            var cats = ResourcePackContentBrowser.ListContents(zip);
            cats.Should().NotBeEmpty();
            cats.Should().Contain(c => c.Category == "Textures" && c.FileCount == 2);
            cats.Should().Contain(c => c.Category == "Models" && c.FileCount == 1);
            cats.Should().Contain(c => c.Category == "Sounds" && c.FileCount == 1);
            // pack.mcmeta and pack.png must be excluded from the listing.
            cats.SelectMany(c => c.Files).Should().NotContain("pack.mcmeta");
            cats.SelectMany(c => c.Files).Should().NotContain("pack.png");
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void ListContents_Largest_Category_First()
    {
        string zip = MakePack(
            ("assets/minecraft/textures/a.png", 10),
            ("assets/minecraft/textures/b.png", 10),
            ("assets/minecraft/textures/c.png", 10),
            ("assets/minecraft/sounds/x.ogg", 100)
        );
        try
        {
            var cats = ResourcePackContentBrowser.ListContents(zip);
            cats[0].Category.Should().Be("Textures"); // 3 files > 1 file
            cats[0].FileCount.Should().Be(3);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void ListContents_Returns_Empty_For_Missing_File()
    {
        ResourcePackContentBrowser.ListContents(Path.Combine(Path.GetTempPath(), "ghost.zip")).Should().BeEmpty();
    }

    [Fact]
    public void GetSummary_Returns_Correct_Counts()
    {
        string zip = MakePack(
            ("assets/minecraft/textures/a.png", 100),
            ("assets/minecraft/models/b.json", 50)
        );
        try
        {
            var summary = ResourcePackContentBrowser.GetSummary(zip);
            summary.TotalFiles.Should().Be(2);
            summary.TotalSizeBytes.Should().Be(150);
            summary.CategoryCount.Should().Be(2);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void ClassifyEntry_Recognizes_Known_Categories()
    {
        ResourcePackContentBrowser.ClassifyEntry("assets/minecraft/textures/block/stone.png").Should().Be("Textures");
        ResourcePackContentBrowser.ClassifyEntry("assets/minecraft/models/item/diamond.json").Should().Be("Models");
        ResourcePackContentBrowser.ClassifyEntry("assets/minecraft/sounds/music.ogg").Should().Be("Sounds");
        ResourcePackContentBrowser.ClassifyEntry("readme.txt").Should().Be("Other");
    }

    [Fact]
    public void ListContents_Skips_Directory_Entries()
    {
        // Zero-length entries ending in / are directory placeholders; they must not appear as files.
        string zipPath = Path.Combine(Path.GetTempPath(), "rpcb-dir-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("assets/minecraft/textures/");
            var entry = archive.CreateEntry("assets/minecraft/textures/stone.png");
            using var s = entry.Open();
            s.Write(new byte[64], 0, 64);
        }
        try
        {
            var cats = ResourcePackContentBrowser.ListContents(zipPath);
            cats.Should().ContainSingle();
            cats[0].FileCount.Should().Be(1); // only stone.png, not the dir entry
        }
        finally { File.Delete(zipPath); }
    }
}
