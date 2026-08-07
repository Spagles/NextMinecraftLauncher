using System.IO;
using System.IO.Compression;
using System.Text;
using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="WorldSettingsManager"/> — reads difficulty + gamerules from a world's
/// level.dat using the minimal NBT byte scanner. Tests synthesize a real gzip-wrapped NBT payload.
/// </summary>
public class WorldSettingsManagerTests
{
    private static string MakeLevelDat(byte difficulty, params (string Rule, string Value)[] gamerules)
        => MakeLevelDat(difficulty, gameType: 0, gamerules);

    /// <summary>Build a level.dat carrying Difficulty (TAG_Byte), GameType (TAG_Int), and gamerules.</summary>
    private static string MakeLevelDat(byte difficulty, int gameType, params (string Rule, string Value)[] gamerules)
    {
        string worldDir = Path.Combine(Path.GetTempPath(), "nml-ws-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worldDir);

        // Build a minimal NBT: TAG_Compound root → Data compound → Difficulty byte + GameType int + GameRules compound.
        using var body = new MemoryStream();
        // Root: TAG_Compound (10) + empty name (len=0).
        body.WriteByte(10); body.WriteByte(0); body.WriteByte(0);
        // Data: TAG_Compound (10) + name "Data".
        WriteName(body, 10, "Data");
        // Difficulty: TAG_Byte (1) + name "Difficulty" + value.
        WriteName(body, 1, "Difficulty");
        body.WriteByte(difficulty);
        // GameType: TAG_Int (3) + name "GameType" + 4-byte big-endian value.
        WriteName(body, 3, "GameType");
        body.WriteByte((byte)((gameType >> 24) & 0xFF));
        body.WriteByte((byte)((gameType >> 16) & 0xFF));
        body.WriteByte((byte)((gameType >> 8) & 0xFF));
        body.WriteByte((byte)(gameType & 0xFF));
        // GameRules: TAG_Compound (10) + name "GameRules".
        WriteName(body, 10, "GameRules");
        foreach (var (rule, value) in gamerules)
        {
            WriteStringTag(body, rule, value);
        }
        body.WriteByte(0); // end GameRules
        body.WriteByte(0); // end Data
        body.WriteByte(0); // end root

        byte[] raw = body.ToArray();
        // Gzip-wrap.
        string levelDat = Path.Combine(worldDir, "level.dat");
        using (var fs = File.OpenWrite(levelDat))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            gz.Write(raw, 0, raw.Length);

        return worldDir;
    }

    private static void WriteName(Stream s, byte tagId, string name)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        s.WriteByte(tagId);
        s.WriteByte((byte)(nameBytes.Length >> 8));
        s.WriteByte((byte)(nameBytes.Length & 0xFF));
        s.Write(nameBytes, 0, nameBytes.Length);
    }

    private static void WriteStringTag(Stream s, string name, string value)
    {
        WriteName(s, 8, name); // TAG_String (8) + name
        byte[] valBytes = Encoding.UTF8.GetBytes(value);
        s.WriteByte((byte)(valBytes.Length >> 8));
        s.WriteByte((byte)(valBytes.Length & 0xFF));
        s.Write(valBytes, 0, valBytes.Length);
    }

    [Theory]
    [InlineData(0, "peaceful")]
    [InlineData(1, "easy")]
    [InlineData(2, "normal")]
    [InlineData(3, "hard")]
    public void Read_Extracts_Difficulty(byte diffByte, string expectedName)
    {
        string dir = MakeLevelDat(diffByte);
        try
        {
            var settings = WorldSettingsManager.Read(dir);
            settings.Difficulty.Should().Be(expectedName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Read_Extracts_Gamerules()
    {
        string dir = MakeLevelDat(2,
            ("keepInventory", "true"),
            ("doDaylightCycle", "false"),
            ("doMobSpawning", "true"));
        try
        {
            var settings = WorldSettingsManager.Read(dir);
            settings.IsRuleEnabled("keepInventory").Should().BeTrue();
            settings.IsRuleEnabled("doDaylightCycle").Should().BeFalse();
            settings.IsRuleEnabled("doMobSpawning").Should().BeTrue();
            // A rule not in the file → false.
            settings.IsRuleEnabled("doFireTick").Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Read_Returns_Defaults_When_No_LevelDat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-empty-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var settings = WorldSettingsManager.Read(dir);
            settings.Difficulty.Should().Be("normal");
            settings.GameRules.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("peaceful", 0)]
    [InlineData("easy", 1)]
    [InlineData("normal", 2)]
    [InlineData("hard", 3)]
    [InlineData("unknown", 2)] // falls back to normal
    public void DifficultyByte_Converts_Name_To_Value(string name, byte expected)
    {
        WorldSettingsManager.DifficultyByte(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "peaceful")]
    [InlineData(1, "easy")]
    [InlineData(2, "normal")]
    [InlineData(3, "hard")]
    [InlineData(99, "normal")] // unknown → normal
    public void DifficultyName_Converts_Value_To_Name(byte value, string expected)
    {
        WorldSettingsManager.DifficultyName(value).Should().Be(expected);
    }

    // ===== Write path (read → edit → write → read-back round trip) =====

    [Theory]
    [InlineData("normal", "hard")]
    [InlineData("easy", "peaceful")]
    [InlineData("hard", "normal")]
    public void Write_Persists_Difficulty_Change_And_Round_Trips(string from, string to)
    {
        // Start with one difficulty, write a different one, read back — the change must survive.
        string dir = MakeLevelDat(WorldSettingsManager.DifficultyByte(from));
        try
        {
            WorldSettingsManager.Read(dir).Difficulty.Should().Be(from, "precondition: initial value");
            WorldSettingsManager.Write(dir, new WorldSettings { Difficulty = to });
            WorldSettingsManager.Read(dir).Difficulty.Should().Be(to, "the write must persist the new difficulty");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_Toggles_Gamerules_And_Round_Trips_Both_Ways()
    {
        // keepInventory=true, doDaylightCycle=false initially; flip both and confirm read-back.
        string dir = MakeLevelDat(2,
            ("keepInventory", "true"),
            ("doDaylightCycle", "false"));
        try
        {
            var before = WorldSettingsManager.Read(dir);
            before.IsRuleEnabled("keepInventory").Should().BeTrue();
            before.IsRuleEnabled("doDaylightCycle").Should().BeFalse();

            var edited = before with
            {
                GameRules = new Dictionary<string, string>
                {
                    ["keepInventory"] = "false",     // toggle OFF
                    ["doDaylightCycle"] = "true",    // toggle ON
                }
            };
            WorldSettingsManager.Write(dir, edited);

            var after = WorldSettingsManager.Read(dir);
            after.IsRuleEnabled("keepInventory").Should().BeFalse("toggle must persist");
            after.IsRuleEnabled("doDaylightCycle").Should().BeTrue("toggle must persist");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_Preserves_Neighboring_Gamerules_Not_Being_Edited()
    {
        // doMobSpawning is not one of the edited rules but sits between the edited ones in the file —
        // it must survive the byte-shifting splice intact (guards the ReplaceStringTag splice logic).
        string dir = MakeLevelDat(2,
            ("keepInventory", "true"),
            ("doMobSpawning", "true"),   // should be untouched by the write
            ("doDaylightCycle", "false"));
        try
        {
            var read = WorldSettingsManager.Read(dir);
            var edited = read with { GameRules = new Dictionary<string, string>
            {
                ["keepInventory"] = "false",
                ["doDaylightCycle"] = "true",
            } };
            WorldSettingsManager.Write(dir, edited);

            var after = WorldSettingsManager.Read(dir);
            after.IsRuleEnabled("doMobSpawning").Should().BeTrue("an unedited gamerule between edited ones must survive");
            after.IsRuleEnabled("keepInventory").Should().BeFalse();
            after.IsRuleEnabled("doDaylightCycle").Should().BeTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_Preserves_Difficulty_Byte_When_Only_Gamerules_Change()
    {
        // Editing gamerules must not disturb the Difficulty byte tag (it lives earlier in the NBT).
        string dir = MakeLevelDat(3 /* hard */, ("keepInventory", "false"));
        try
        {
            WorldSettingsManager.Write(dir, new WorldSettings
            {
                Difficulty = "hard", // unchanged
                GameRules = new Dictionary<string, string> { ["keepInventory"] = "true" }
            });
            WorldSettingsManager.Read(dir).Difficulty.Should().Be("hard", "difficulty must be untouched when only gamerules change");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_Throws_When_LevelDat_Missing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-noleveldat-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var act = () => WorldSettingsManager.Write(dir, new WorldSettings { Difficulty = "easy" });
            act.Should().Throw<FileNotFoundException>("writing to a world with no level.dat is an error");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_Is_Atomic_LevelDat_Stays_Valid_On_Success()
    {
        // After a successful write the level.dat must still be valid gzip NBT (readable back), and
        // no .tmp file should be left behind (the atomic-rename contract).
        string dir = MakeLevelDat(1, ("keepInventory", "false"));
        try
        {
            WorldSettingsManager.Write(dir, new WorldSettings
            {
                Difficulty = "normal",
                GameRules = new Dictionary<string, string> { ["keepInventory"] = "true" }
            });

            File.Exists(Path.Combine(dir, "level.dat")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "level.dat.tmp")).Should().BeFalse("the temp file must be cleaned up");
            // Re-read proves the gzip + NBT is still structurally valid.
            WorldSettingsManager.Read(dir).Difficulty.Should().Be("normal");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ===== GameType (TAG_Int) read + write round-trip =====

    [Theory]
    [InlineData(0, "survival")]
    [InlineData(1, "creative")]
    [InlineData(2, "adventure")]
    [InlineData(3, "spectator")]
    public void Read_Extracts_GameType(int gameTypeByte, string expectedName)
    {
        string dir = MakeLevelDat(2, gameTypeByte);
        try
        {
            WorldSettingsManager.Read(dir).GameType.Should().Be(expectedName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("survival", "creative")]
    [InlineData("spectator", "survival")]
    [InlineData("adventure", "spectator")]
    public void Write_Persists_GameType_Change_And_Round_Trips(string from, string to)
    {
        string dir = MakeLevelDat(2, WorldSettingsManager.GameTypeInt(from));
        try
        {
            WorldSettingsManager.Read(dir).GameType.Should().Be(from, "precondition");
            WorldSettingsManager.Write(dir, new WorldSettings { Difficulty = "normal", GameType = to });
            WorldSettingsManager.Read(dir).GameType.Should().Be(to, "the GameType change must persist");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("survival", 0)]
    [InlineData("creative", 1)]
    [InlineData("adventure", 2)]
    [InlineData("spectator", 3)]
    [InlineData("unknown", 0)] // unknown → survival (0)
    public void GameTypeInt_Converts_Name_To_Value(string name, int expected)
        => WorldSettingsManager.GameTypeInt(name).Should().Be(expected);

    [Theory]
    [InlineData(0, "survival")]
    [InlineData(1, "creative")]
    [InlineData(2, "adventure")]
    [InlineData(3, "spectator")]
    [InlineData(99, "survival")] // unknown → survival
    public void GameTypeName_Converts_Value_To_Name(int value, string expected)
        => WorldSettingsManager.GameTypeName(value).Should().Be(expected);

    [Fact]
    public void Write_GameType_Does_Not_Disturb_Difficulty_Or_Gamerules()
    {
        // Editing GameType (TAG_Int) must not disturb Difficulty (TAG_Byte) or a gamerule that sit
        // near it in the NBT — the in-place 4-byte overwrite must land on exactly the GameType value.
        string dir = MakeLevelDat(3 /* hard */, gameType: 1 /* creative */, ("keepInventory", "true"));
        try
        {
            WorldSettingsManager.Write(dir, new WorldSettings
            {
                Difficulty = "hard", // unchanged
                GameType = "spectator",
                GameRules = new Dictionary<string, string> { ["keepInventory"] = "true" } // unchanged
            });
            var after = WorldSettingsManager.Read(dir);
            after.GameType.Should().Be("spectator");
            after.Difficulty.Should().Be("hard", "difficulty must survive the GameType edit");
            after.IsRuleEnabled("keepInventory").Should().BeTrue("gamerule must survive the GameType edit");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Write_GameType_Preserves_Big_Endian_Byte_Order()
    {
        // The TAG_Int is 4-byte big-endian; writing spectator (3) must produce [0,0,0,3], not a
        // little-endian [3,0,0,0] that would misread as a huge negative number. Verify by round trip.
        string dir = MakeLevelDat(0, gameType: 0);
        try
        {
            WorldSettingsManager.Write(dir, new WorldSettings { Difficulty = "normal", GameType = "spectator" });
            // Read raw NBT back and confirm the 4 bytes after "GameType" are big-endian 3.
            using var fs = File.OpenRead(Path.Combine(dir, "level.dat"));
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            byte[] nbt = ms.ToArray();
            int off = FindGameTypeOffset(nbt);
            off.Should().BeGreaterThan(0);
            (nbt[off], nbt[off + 1], nbt[off + 2], nbt[off + 3])
                .Should().Be((0, 0, 0, 3), "spectator is big-endian int 3");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static int FindGameTypeOffset(byte[] nbt)
    {
        byte[] needle = Encoding.ASCII.GetBytes("GameType");
        for (int i = 1; i < nbt.Length - needle.Length - 7; i++)
        {
            if (nbt[i] != 0x03) continue; // TAG_Int id
            if (nbt[i + 1] != 0x00 || nbt[i + 2] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (nbt[i + 3 + j] != needle[j]) { match = false; break; }
            if (match) return i + 3 + needle.Length;
        }
        return -1;
    }
}
