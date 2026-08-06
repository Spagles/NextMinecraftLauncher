using System.IO;
using System.IO.Compression;
using System.Text;
using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="WorldSeedReader"/> — reads the world seed (RandomSeed TAG_Long) from a
/// world's level.dat. Tests synthesize a real gzip-wrapped NBT payload with the seed.
/// </summary>
public class WorldSeedReaderTests
{
    private static string MakeLevelDatWithSeed(long seed)
    {
        string worldDir = Path.Combine(Path.GetTempPath(), "nml-seed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worldDir);

        using var body = new MemoryStream();
        // Root TAG_Compound (10) + empty name.
        body.WriteByte(10); body.WriteByte(0); body.WriteByte(0);
        // Data TAG_Compound (10) + name "Data".
        WriteName(body, 10, "Data");
        // RandomSeed TAG_Long (4) + name "RandomSeed" + 8-byte BE value.
        WriteName(body, 4, "RandomSeed");
        byte[] longBytes = BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(seed).Reverse().ToArray()
            : BitConverter.GetBytes(seed);
        body.Write(longBytes, 0, 8);
        // Also add a LevelName string so the NBT has some structure.
        WriteName(body, 8, "LevelName");
        byte[] nameBytes = Encoding.UTF8.GetBytes("TestWorld");
        body.WriteByte((byte)(nameBytes.Length >> 8));
        body.WriteByte((byte)(nameBytes.Length & 0xFF));
        body.Write(nameBytes, 0, nameBytes.Length);
        body.WriteByte(0); // end Data
        body.WriteByte(0); // end root

        byte[] raw = body.ToArray();
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

    [Fact]
    public void ReadSeed_Extracts_Positive_Seed()
    {
        string dir = MakeLevelDatWithSeed(1234567890L);
        try
        {
            WorldSeedReader.ReadSeed(dir).Should().Be(1234567890L);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadSeed_Extracts_Negative_Seed()
    {
        string dir = MakeLevelDatWithSeed(-987654321L);
        try
        {
            WorldSeedReader.ReadSeed(dir).Should().Be(-987654321L);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReadSeed_Returns_Null_When_No_LevelDat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-noseed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            WorldSeedReader.ReadSeed(dir).Should().BeNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(-1L, "-1")]
    [InlineData(42L, "42")]
    public void FormatSeed_Converts_To_String(long seed, string expected)
    {
        WorldSeedReader.FormatSeed(seed).Should().Be(expected);
    }

    [Fact]
    public void FormatSeed_Null_Returns_Unknown()
    {
        WorldSeedReader.FormatSeed(null).Should().Be("unknown");
    }
}
