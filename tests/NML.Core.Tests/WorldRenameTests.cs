using System.IO;
using System.IO.Compression;
using System.Text;
using NML.Core;
using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// End-to-end verification of the world-rename flow (HMCL parity): editing the in-game display
/// name via the <c>Data.LevelName</c> NBT tag, plus renaming the on-disk save folder — and the
/// two deconflict so a rename never clobbers an existing world.
/// </summary>
public class WorldRenameTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-rename-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Build a .minecraft root with a saves/&lt;world&gt;/level.dat carrying a LevelName.</summary>
    private static (string mcRoot, string worldDir) BuildWorld(string folderName, string levelName)
    {
        string mcRoot = TempDir();
        string savesDir = Path.Combine(mcRoot, "saves");
        string worldDir = Path.Combine(savesDir, folderName);
        Directory.CreateDirectory(worldDir);
        // A non-level.dat file so the world looks real.
        File.WriteAllBytes(Path.Combine(worldDir, "session.lock"), new byte[] { 1, 2, 3 });
        WriteLevelDat(worldDir, levelName);
        return (mcRoot, worldDir);
    }

    /// <summary>Write a gzip-NBT level.dat with a Data.LevelName string tag (minimal NBT).</summary>
    private static void WriteLevelDat(string worldDir, string levelName)
    {
        using var body = new MemoryStream();
        // Root TAG_Compound + empty name.
        body.WriteByte(10); body.WriteByte(0); body.WriteByte(0);
        // Data TAG_Compound.
        body.WriteByte(10);
        WriteName(body, "Data");
        // LevelName TAG_String.
        body.WriteByte(8);
        WriteName(body, "LevelName");
        WriteStringValue(body, levelName);
        body.WriteByte(0); // end Data
        body.WriteByte(0); // end root

        byte[] raw = body.ToArray();
        string levelDat = Path.Combine(worldDir, "level.dat");
        using (var fs = File.Create(levelDat))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            gz.Write(raw, 0, raw.Length);
    }

    private static void WriteName(Stream s, string name)
    {
        byte[] b = Encoding.ASCII.GetBytes(name);
        s.WriteByte((byte)(b.Length >> 8));
        s.WriteByte((byte)(b.Length & 0xFF));
        s.Write(b, 0, b.Length);
    }

    private static void WriteStringValue(Stream s, string value)
    {
        byte[] b = Encoding.UTF8.GetBytes(value);
        s.WriteByte((byte)(b.Length >> 8));
        s.WriteByte((byte)(b.Length & 0xFF));
        s.Write(b, 0, b.Length);
    }

    [Fact]
    public void WriteLevelName_Round_Trips_Via_Reader()
    {
        // Edit LevelName in level.dat, then read it back via the public reader — must match.
        var (_, worldDir) = BuildWorld("OldFolder", "Old Name");
        try
        {
            WorldSettingsManager.WriteLevelName(worldDir, "New Adventure");
            WorldMetadataReader.ReadLevelName(worldDir).Should().Be("New Adventure");
        }
        finally { Directory.Delete(worldDir, recursive: true); }
    }

    [Theory]
    [InlineData("Short")]
    [InlineData("A Much Longer World Name With Spaces and unicode 日本語")]
    public void WriteLevelName_Round_Trips_Across_Name_Lengths(string newName)
    {
        // The NBT string-splice rebuilds the length prefix when the new value differs in length
        // from the old — exercise that across short and long names.
        var (_, worldDir) = BuildWorld("w", "X");
        try
        {
            WorldSettingsManager.WriteLevelName(worldDir, newName);
            WorldMetadataReader.ReadLevelName(worldDir).Should().Be(newName);
        }
        finally { Directory.Delete(worldDir, recursive: true); }
    }

    [Fact]
    public void WriteLevelName_Does_Not_Disturb_Other_Tags()
    {
        // level.dat has more than LevelName; the splice must leave the rest structurally valid
        // (the gzip stays readable and the Difficulty tag survives). We synthesize a level.dat with
        // both Difficulty and LevelName and confirm both read correctly after the rename.
        string worldDir = Path.Combine(TempDir(), "saves", "World");
        Directory.CreateDirectory(worldDir);
        try
        {
            WriteLevelDatWithDifficulty(worldDir, "Original", (byte)3);
            WorldSettingsManager.WriteLevelName(worldDir, "Renamed");
            WorldMetadataReader.ReadLevelName(worldDir).Should().Be("Renamed");
            WorldSettingsManager.Read(worldDir).Difficulty.Should().Be("hard",
                "an unrelated tag (Difficulty) must survive the LevelName splice intact");
        }
        finally { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(worldDir))!, recursive: true); }
    }

    private static void WriteLevelDatWithDifficulty(string worldDir, string levelName, byte difficulty)
    {
        using var body = new MemoryStream();
        body.WriteByte(10); body.WriteByte(0); body.WriteByte(0); // root
        body.WriteByte(10); WriteName(body, "Data");              // Data
        body.WriteByte(8); WriteName(body, "LevelName"); WriteStringValue(body, levelName); // LevelName string
        body.WriteByte(1); WriteName(body, "Difficulty"); body.WriteByte(difficulty);       // Difficulty byte
        body.WriteByte(0); body.WriteByte(0);
        byte[] raw = body.ToArray();
        using (var fs = File.Create(Path.Combine(worldDir, "level.dat")))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            gz.Write(raw, 0, raw.Length);
    }

    [Fact]
    public void WriteLevelName_Throws_When_LevelDat_Missing()
    {
        string worldDir = Path.Combine(TempDir(), "empty");
        Directory.CreateDirectory(worldDir);
        try
        {
            var act = () => WorldSettingsManager.WriteLevelName(worldDir, "Anything");
            act.Should().Throw<FileNotFoundException>();
        }
        finally { Directory.Delete(worldDir, recursive: true); }
    }

    // ===== GameContentBrowser.RenameWorld (LevelName edit + folder rename + deconflict) =====

    [Fact]
    public void RenameWorld_Edits_LevelName_And_Renames_Folder()
    {
        var (mcRoot, worldDir) = BuildWorld("MyWorld", "Old Name");
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(mcRoot));
            string result = browser.RenameWorld(worldDir, "Brand New World");

            // The folder was renamed to a safe version of the new name.
            result.Should().EndWith(Path.Combine("saves", "Brand New World"));
            Directory.Exists(worldDir).Should().BeFalse("the old folder is gone");
            Directory.Exists(result).Should().BeTrue("the new folder exists");
            // The in-game name was rewritten.
            WorldMetadataReader.ReadLevelName(result).Should().Be("Brand New World");
            // Other files came along for the move.
            File.Exists(Path.Combine(result, "session.lock")).Should().BeTrue();
        }
        finally { Directory.Delete(mcRoot, recursive: true); }
    }

    [Fact]
    public void RenameWorld_Deconflicts_When_Target_Folder_Exists()
    {
        // Two worlds: rename the first to a name that collides with the second's folder → suffix.
        var (mcRoot, world1) = BuildWorld("World1", "First");
        string world2 = Path.Combine(mcRoot, "saves", "Collision");
        Directory.CreateDirectory(world2);
        WriteLevelDat(world2, "Second");
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(mcRoot));
            string result = browser.RenameWorld(world1, "Collision"); // folder name "Collision" taken

            result.Should().EndWith(Path.Combine("saves", "Collision (1)"),
                "the rename must not clobber the existing 'Collision' folder");
            Directory.Exists(world2).Should().BeTrue("the pre-existing world is untouched");
            // LevelName is written verbatim (the user-typed name), even though the folder got a suffix.
            WorldMetadataReader.ReadLevelName(result).Should().Be("Collision");
        }
        finally { Directory.Delete(mcRoot, recursive: true); }
    }

    [Fact]
    public void RenameWorld_Sanitizes_Illegal_Folder_Chars()
    {
        // A name with path separators / illegal chars must become a safe single folder segment,
        // while the in-game LevelName keeps the user's original (with a slash it'd be verbatim text).
        var (mcRoot, worldDir) = BuildWorld("Plain", "Plain");
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(mcRoot));
            string result = browser.RenameWorld(worldDir, "a/b:c?d");

            // Folder name must be a single safe segment.
            string folderName = Path.GetFileName(result);
            folderName.Should().NotContain("/", "forward slash stripped");
            folderName.Should().NotContain("\\", "backslash stripped");
            folderName.Should().NotContain(":", "colon stripped");
            folderName.Should().NotContain("?", "question mark stripped");
            Directory.Exists(result).Should().BeTrue();
        }
        finally { Directory.Delete(mcRoot, recursive: true); }
    }

    [Fact]
    public void RenameWorld_Noop_When_Folder_Name_Already_Matches()
    {
        // Renaming to a display name whose sanitized folder name equals the current folder →
        // LevelName edits, but the folder is NOT moved (no deconflict suffix, same path).
        var (mcRoot, worldDir) = BuildWorld("Same", "Old Display");
        try
        {
            var browser = new GameContentBrowser(new MinecraftDirectory(mcRoot));
            string result = browser.RenameWorld(worldDir, "Same");

            result.Should().Be(worldDir, "folder name unchanged when target == source");
            WorldMetadataReader.ReadLevelName(result).Should().Be("Same", "LevelName still updated");
        }
        finally { Directory.Delete(mcRoot, recursive: true); }
    }
}
