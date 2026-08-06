using System.IO;
using System.IO.Compression;

namespace NML.Core;

/// <summary>
/// Reads a Minecraft world's display metadata: the level name embedded in
/// <c>level.dat</c> (a gzip-wrapped NBT compound at <c>/.Data.LevelName</c>) and the
/// optional <c>icon.png</c> preview image stored beside it.
/// <para>
/// This is a deliberately minimal NBT reader: it only needs to descend into the two
/// nested compounds (<c>root</c> → <c>Data</c>) and pull the single <c>LevelName</c>
/// string tag, so it avoids pulling in a full NBT library. A malformed or missing
/// <c>level.dat</c> is tolerated — the caller falls back to the folder name.
/// </para>
/// </summary>
public static class WorldMetadataReader
{
    /// <summary>
    /// Read the world's display name from <c>{worldDir}/level.dat</c>. Returns null when the
    /// file is missing, not gzipped NBT, or does not contain a <c>Data.LevelName</c> string tag.
    /// </summary>
    public static string? ReadLevelName(string worldDir)
    {
        string levelDat = Path.Combine(worldDir, "level.dat");
        if (!File.Exists(levelDat)) return null;

        try
        {
            // level.dat is gzip-compressed NBT. Decompress, then scan for the LevelName tag.
            using var fs = File.OpenRead(levelDat);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            byte[] nbt = ms.ToArray();
            return FindLevelName(nbt);
        }
        catch
        {
            return null; // corrupt or unexpected format — caller falls back to folder name.
        }
    }

    /// <summary>
    /// Absolute path to the world's <c>icon.png</c> if it exists, else null. The UI binds an
    /// Image source to this; when null a generated placeholder is shown instead.
    /// </summary>
    public static string? ReadIconPath(string worldDir)
    {
        string icon = Path.Combine(worldDir, "icon.png");
        return File.Exists(icon) ? icon : null;
    }

    /// <summary>
    /// Walk the decompressed NBT payload looking for the <c>LevelName</c> string tag without
    /// building a full document tree. NBT strings are prefixed by a 2-byte big-endian length;
    /// a string tag is the byte <c>0x08</c> followed by the name length+bytes, then the value
    /// length+bytes. We scan for the ASCII run <c>"LevelName"</c> preceded by a string-tag id.
    /// </summary>
    private static string? FindLevelName(byte[] nbt)
    {
        // Locate the tag-name bytes "LevelName" preceded by the TAG_String id (0x08).
        byte[] needle = System.Text.Encoding.ASCII.GetBytes("LevelName");
        for (int i = 1; i < nbt.Length - needle.Length - 4; i++)
        {
            if (nbt[i] != 0x08) continue; // TAG_String id
            // Match the name length (2-byte BE) == needle.Length followed by the name bytes.
            if (nbt[i + 1] != 0x00 || nbt[i + 2] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (nbt[i + 3 + j] != needle[j]) { match = false; break; }
            if (!match) continue;

            // The value follows: 2-byte BE length + UTF-8 bytes (Java-modified, but plain ASCII
            // level names round-trip fine).
            int valLenOff = i + 3 + needle.Length;
            if (valLenOff + 2 > nbt.Length) return null;
            int valLen = (nbt[valLenOff] << 8) | nbt[valLenOff + 1];
            if (valLen <= 0 || valLenOff + 2 + valLen > nbt.Length) return null;
            return System.Text.Encoding.UTF8.GetString(nbt, valLenOff + 2, valLen);
        }
        return null;
    }
}
