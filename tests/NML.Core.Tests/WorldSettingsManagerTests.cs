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
    {
        string worldDir = Path.Combine(Path.GetTempPath(), "nml-ws-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worldDir);

        // Build a minimal NBT: TAG_Compound root → Data compound → Difficulty byte + GameRules compound.
        using var body = new MemoryStream();
        // Root: TAG_Compound (10) + empty name (len=0).
        body.WriteByte(10); body.WriteByte(0); body.WriteByte(0);
        // Data: TAG_Compound (10) + name "Data".
        WriteName(body, 10, "Data");
        // Difficulty: TAG_Byte (1) + name "Difficulty" + value.
        WriteName(body, 1, "Difficulty");
        body.WriteByte(difficulty);
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
}
