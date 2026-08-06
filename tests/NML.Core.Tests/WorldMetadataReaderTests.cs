using System.IO;
using System.IO.Compression;
using System.Text;
using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the world-save grid feature: <see cref="WorldMetadataReader"/> extracts the
/// LevelName from a real gzip-wrapped level.dat NBT and resolves icon.png, and
/// <see cref="GameContentBrowser.ListSaves"/> enriches each <see cref="GameSave"/> with the
/// display name (falling back to the folder name) and preview-icon path.
/// <para>
/// The test synthesizes a minimal valid NBT payload by hand so it needs no external world
/// fixture or NBT library — the same byte layout the scanner walks in production.
/// </para>
/// </summary>
public class WorldMetadataReaderTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-world-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Build a minimal gzip-wrapped NBT compound containing a <c>Data</c> sub-compound with a
    /// <c>LevelName</c> TAG_String. NBT is big-endian, so all length prefixes are written MSB
    /// first — matching what the scanner reads in production. Only the bytes the scanner cares
    /// about are correct; the rest is structurally plausible NBT.
    /// </summary>
    private static byte[] BuildLevelDatBe(string levelName)
    {
        using var ms = new MemoryStream();
        // Root TAG_Compound: id(10) + name (empty: len=0, 2 bytes BE)
        ms.WriteByte(10);
        ms.WriteByte(0); ms.WriteByte(0);
        // TAG_Compound "Data": id(10) + name
        ms.WriteByte(10);
        WriteNameBe(ms, "Data");
        // TAG_String "LevelName": id(8) + name + value
        ms.WriteByte(8);
        WriteNameBe(ms, "LevelName");
        WriteStringBe(ms, levelName);
        ms.WriteByte(0); // end Data
        ms.WriteByte(0); // end root

        byte[] raw = ms.ToArray();
        using var gzMs = new MemoryStream();
        using (var gz = new GZipStream(gzMs, CompressionLevel.Optimal))
            gz.Write(raw, 0, raw.Length);
        return gzMs.ToArray();
    }

    private static void WriteNameBe(Stream s, string name)
    {
        byte[] b = Encoding.UTF8.GetBytes(name);
        s.WriteByte((byte)(b.Length >> 8));
        s.WriteByte((byte)(b.Length & 0xFF));
        s.Write(b, 0, b.Length);
    }

    private static void WriteStringBe(Stream s, string str)
    {
        byte[] b = Encoding.UTF8.GetBytes(str);
        s.WriteByte((byte)(b.Length >> 8));
        s.WriteByte((byte)(b.Length & 0xFF));
        s.Write(b, 0, b.Length);
    }

    [Fact]
    public void ReadLevelName_Extracts_From_Synthesized_LevelDat()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "level.dat"), BuildLevelDatBe("New World"));
            string? name = WorldMetadataReader.ReadLevelName(dir);
            name.Should().Be("New World");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadLevelName_Returns_Null_When_LevelDat_Missing()
    {
        string dir = TempDir();
        try
        {
            WorldMetadataReader.ReadLevelName(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadLevelName_Returns_Null_For_Corrupt_LevelDat()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "level.dat"), new byte[] { 0, 1, 2, 3 }); // not gzip
            WorldMetadataReader.ReadLevelName(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadIconPath_Returns_Path_When_Icon_Exists()
    {
        string dir = TempDir();
        try
        {
            string icon = Path.Combine(dir, "icon.png");
            File.WriteAllBytes(icon, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
            WorldMetadataReader.ReadIconPath(dir).Should().Be(icon);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadIconPath_Returns_Null_When_No_Icon()
    {
        string dir = TempDir();
        try
        {
            WorldMetadataReader.ReadIconPath(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ListSaves_Populates_DisplayName_And_Icon()
    {
        // Build a fake instance root with one world that has a level.dat + icon.png, and one
        // bare folder (no level.dat) that should fall back to its folder name + null icon.
        string root = TempDir();
        string savesDir = Path.Combine(root, "saves");
        string w1 = Path.Combine(savesDir, "World1");
        string w2 = Path.Combine(savesDir, "World2");
        Directory.CreateDirectory(w1);
        Directory.CreateDirectory(w2);
        File.WriteAllBytes(Path.Combine(w1, "level.dat"), BuildLevelDatBe("My Cool World"));
        File.WriteAllBytes(Path.Combine(w1, "icon.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(root));
            var saves = browser.ListSaves();
            saves.Should().HaveCount(2);

            var s1 = saves.Single(s => s.Name == "World1");
            s1.DisplayName.Should().Be("My Cool World");
            s1.PreviewIconPath.Should().EndWith("icon.png");

            // Bare folder: display name falls back to folder name, icon is null.
            var s2 = saves.Single(s => s.Name == "World2");
            s2.DisplayName.Should().Be("World2");
            s2.PreviewIconPath.Should().BeNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
