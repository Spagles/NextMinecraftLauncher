using System.IO;
using System.IO.Compression;
using System.Text;

namespace NML.Core.Game;

/// <summary>
/// Reads a world's random seed from its level.dat. Minecraft stores this as a TAG_Long named
/// "RandomSeed" under the Data compound. The launcher can display it and let the player share it
/// (common request for world-seed sharing). Uses the same minimal NBT byte-scanner as
/// WorldMetadataReader / WorldSettingsManager — no full NBT library.
/// <para>
/// A TAG_Long in NBT is 8 bytes big-endian. We scan for the "RandomSeed" tag name preceded by a
/// TAG_Long id (0x04), then read the following 8 bytes.
/// </para>
/// </summary>
public static class WorldSeedReader
{
    /// <summary>Read the world seed (a 64-bit integer) from level.dat. Returns null when the file is
    /// missing, unreadable, or the tag isn't found.</summary>
    public static long? ReadSeed(string worldDir)
    {
        string levelDat = Path.Combine(worldDir, "level.dat");
        if (!File.Exists(levelDat)) return null;

        try
        {
            using var fs = File.OpenRead(levelDat);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            byte[] nbt = ms.ToArray();
            return FindLongTag(nbt, "RandomSeed");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The seed as a player-facing string. Numeric for Java seeds (e.g. "-123456789").</summary>
    public static string FormatSeed(long? seed) => seed?.ToString() ?? "unknown";

    /// <summary>Find a TAG_Long (0x04) value by its tag name in the decompressed NBT payload.
    /// Returns null when not found.</summary>
    private static long? FindLongTag(byte[] nbt, string tagName)
    {
        byte[] needle = Encoding.ASCII.GetBytes(tagName);
        for (int i = 1; i < nbt.Length - needle.Length - 10; i++)
        {
            if (nbt[i] != 0x04) continue; // TAG_Long id
            if (nbt[i + 1] != 0x00 || nbt[i + 2] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (nbt[i + 3 + j] != needle[j]) { match = false; break; }
            if (!match) continue;

            // The value: 8 bytes big-endian.
            int valOff = i + 3 + needle.Length;
            if (valOff + 8 > nbt.Length) return null;
            long value = 0;
            for (int b = 0; b < 8; b++)
                value = (value << 8) | nbt[valOff + b];
            return value;
        }
        return null;
    }
}
