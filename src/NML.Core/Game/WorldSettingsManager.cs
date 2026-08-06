using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NML.Core.Game;

/// <summary>
/// Reads and writes a world's difficulty + selected gamerules from its level.dat, so the launcher
/// can let the user toggle keepInventory / doDaylightCycle / doMobSpawning / etc. and change the
/// difficulty without launching the game. Uses the same minimal NBT scanner approach as
/// WorldMetadataReader — no full NBT library, just targeted byte-scanning for the tags we need.
/// <para>
/// Difficulty is a Byte tag (0=peaceful, 1=easy, 2=normal, 3=hard). GameRules are a compound of
/// String tags under Data→GameRules, each value is "true"/"false" or a number string.
/// </para>
/// </summary>
public static class WorldSettingsManager
{
    /// <summary>Difficulty names ↔ byte values.</summary>
    public static readonly IReadOnlyDictionary<string, byte> DifficultyValues = new Dictionary<string, byte>
    {
        { "peaceful", 0 }, { "easy", 1 }, { "normal", 2 }, { "hard", 3 },
    };

    /// <summary>Common gamerules the UI exposes as toggles.</summary>
    public static readonly IReadOnlyList<string> ToggleableRules = new[]
    {
        "keepInventory", "doDaylightCycle", "doMobSpawning", "doFireTick",
        "mobGriefing", "doWeatherCycle", "naturalRegeneration", "showDeathMessages",
    };

    /// <summary>Read the difficulty (as a name) and the toggleable gamerules from a world dir's
    /// level.dat. Returns defaults when the file is missing or unreadable.</summary>
    public static WorldSettings Read(string worldDir)
    {
        string levelDat = Path.Combine(worldDir, "level.dat");
        if (!File.Exists(levelDat)) return new WorldSettings();

        try
        {
            using var fs = File.OpenRead(levelDat);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            byte[] nbt = ms.ToArray();

            byte diff = FindByteTag(nbt, "Difficulty", (byte)2);
            var rules = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string rule in ToggleableRules)
            {
                string? value = FindStringTag(nbt, rule);
                if (value is not null) rules[rule] = value;
            }
            return new WorldSettings(DifficultyName(diff), rules);
        }
        catch
        {
            return new WorldSettings();
        }
    }

    /// <summary>Convert a difficulty byte (0–3) to its name.</summary>
    public static string DifficultyName(byte b) => b switch
    {
        0 => "peaceful", 1 => "easy", 2 => "normal", 3 => "hard", _ => "normal",
    };

    /// <summary>Convert a difficulty name to its byte value.</summary>
    public static byte DifficultyByte(string name)
        => DifficultyValues.TryGetValue(name.ToLowerInvariant(), out byte b) ? b : (byte)2;

    // --- NBT byte-scanning helpers (same minimal approach as WorldMetadataReader) ---

    /// <summary>Find a TAG_Byte value by its tag name. Returns the default when not found.</summary>
    private static byte FindByteTag(byte[] nbt, string tagName, byte defaultValue)
    {
        byte[] needle = Encoding.ASCII.GetBytes(tagName);
        for (int i = 1; i < nbt.Length - needle.Length - 3; i++)
        {
            if (nbt[i] != 0x01) continue; // TAG_Byte id
            if (nbt[i + 1] != 0x00 || nbt[i + 2] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (nbt[i + 3 + j] != needle[j]) { match = false; break; }
            if (!match) continue;
            // The value follows the name: 1 byte.
            int valOff = i + 3 + needle.Length;
            if (valOff < nbt.Length) return nbt[valOff];
        }
        return defaultValue;
    }

    /// <summary>Find a TAG_String value by its tag name. Returns null when not found.</summary>
    private static string? FindStringTag(byte[] nbt, string tagName)
    {
        byte[] needle = Encoding.ASCII.GetBytes(tagName);
        for (int i = 1; i < nbt.Length - needle.Length - 5; i++)
        {
            if (nbt[i] != 0x08) continue; // TAG_String id
            if (nbt[i + 1] != 0x00 || nbt[i + 2] != needle.Length) continue;
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (nbt[i + 3 + j] != needle[j]) { match = false; break; }
            if (!match) continue;
            // The value: 2-byte BE length + UTF-8 bytes.
            int valLenOff = i + 3 + needle.Length;
            if (valLenOff + 2 > nbt.Length) return null;
            int valLen = (nbt[valLenOff] << 8) | nbt[valLenOff + 1];
            if (valLen <= 0 || valLenOff + 2 + valLen > nbt.Length) return null;
            return Encoding.UTF8.GetString(nbt, valLenOff + 2, valLen);
        }
        return null;
    }
}

/// <summary>The read difficulty + gamerules from a world's level.dat.</summary>
public sealed record WorldSettings
{
    public string Difficulty { get; init; } = "normal";
    public IReadOnlyDictionary<string, string> GameRules { get; init; } = new Dictionary<string, string>();

    public WorldSettings() { }
    public WorldSettings(string difficulty, IReadOnlyDictionary<string, string> gameRules)
    {
        Difficulty = difficulty;
        GameRules = gameRules ?? new Dictionary<string, string>();
    }

    /// <summary>True when a gamerule is set to "true" (case-insensitive).</summary>
    public bool IsRuleEnabled(string rule)
        => GameRules.TryGetValue(rule, out var v) && string.Equals(v, "true", System.StringComparison.OrdinalIgnoreCase);
}
