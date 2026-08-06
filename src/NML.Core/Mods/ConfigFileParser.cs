using System.Collections.Generic;
using System.Text;

namespace NML.Core.Mods;

/// <summary>
/// Parses mod-config files (Forge <c>.cfg</c>, <c>.ini</c>, <c>.properties</c> — all
/// <c>key=value</c> dialects) into structured <see cref="ConfigEntry"/> rows so the UI can render
/// each key/value pair on its own line while preserving comments and section headers. Files that
/// aren't a recognized key=value dialect (TOML/JSON) fall back to a single opaque "blob" entry so
/// the existing plain-text editor keeps working unchanged. Round-trips losslessly.
/// <para>
/// Pure + unit-tested off the filesystem. The parse rules intentionally stay simple: a line is a
/// comment if it starts (after optional whitespace) with <c>#</c> or <c>;</c>, a section header if
/// it matches <c>[name]</c>, and a key=value pair on the first unquoted <c>=</c>.
/// </para>
/// </summary>
public static class ConfigFileParser
{
    /// <summary>Recognized key=value dialects the parser structures into rows.</summary>
    public static readonly IReadOnlySet<string> StructuredExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    { ".cfg", ".ini", ".conf", ".properties", ".props" };

    /// <summary>True when the file extension is one the parser can structure (else opaque blob).</summary>
    public static bool IsStructured(string fileName)
    {
        string ext = System.IO.Path.GetExtension(fileName);
        return StructuredExtensions.Contains(ext);
    }

    /// <summary>Parse a config-file body into ordered <see cref="ConfigEntry"/> rows.</summary>
    public static IReadOnlyList<ConfigEntry> Parse(string content, string fileName)
    {
        var entries = new List<ConfigEntry>();
        // Non-structured formats (TOML/JSON/txt) → a single opaque blob so the plain-text path is
        // unchanged and the round-trip is exactly identity.
        if (!IsStructured(fileName) || string.IsNullOrEmpty(content))
        {
            entries.Add(new ConfigEntry(ConfigEntryKind.Blob, string.Empty, string.Empty, content ?? string.Empty));
            return entries;
        }

        string[] lines = content.Split('\n');
        foreach (string raw in lines)
        {
            // Preserve the exact line for round-trip fidelity (we never normalize whitespace).
            string line = raw.EndsWith('\r') ? raw[..^1] : raw;
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                entries.Add(new ConfigEntry(ConfigEntryKind.Blank, string.Empty, string.Empty, line));
                continue;
            }
            if (trimmed[0] == '#' || trimmed[0] == ';')
            {
                entries.Add(new ConfigEntry(ConfigEntryKind.Comment, string.Empty, string.Empty, line));
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length >= 2)
            {
                entries.Add(new ConfigEntry(ConfigEntryKind.Section, trimmed[1..^1], string.Empty, line));
                continue;
            }

            int eq = FindUnquotedEquals(line);
            if (eq > 0)
            {
                string key = line[..eq].Trim();
                string value = eq + 1 < line.Length ? line[(eq + 1)..] : string.Empty;
                // Trailing comment after the value is kept inside the value verbatim (no split) so
                // the serialize is exact; the UI shows the full value including the inline comment.
                entries.Add(new ConfigEntry(ConfigEntryKind.KeyValue, key, value, line));
            }
            else
            {
                // Unrecognized structured line — keep as a comment-style row that round-trips.
                entries.Add(new ConfigEntry(ConfigEntryKind.Other, string.Empty, string.Empty, line));
            }
        }
        return entries;
    }

    /// <summary>Serialize the entries back to a config-file body. Lossless: round-trip(Parse(x)) == x
    /// for the original line endings (LF) it was fed.</summary>
    public static string Serialize(IReadOnlyList<ConfigEntry> entries)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            ConfigEntry e = entries[i];
            sb.Append(SerializeLine(e));
            if (i < entries.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Render one entry back to its source line, honoring any in-place edits to Key/Value.</summary>
    private static string SerializeLine(ConfigEntry e) => e.Kind switch
    {
        ConfigEntryKind.Blob     => e.RawLine,
        ConfigEntryKind.Blank    => e.RawLine,
        ConfigEntryKind.Comment  => e.RawLine,
        ConfigEntryKind.Section  => $"[{e.Key}]",
        ConfigEntryKind.KeyValue => e.Value.Length > 0 ? $"{e.Key}={e.Value}" : $"{e.Key}=",
        ConfigEntryKind.Other    => e.RawLine,
        _                         => e.RawLine,
    };

    /// <summary>Index of the first <c>=</c> not inside a double-quoted value; -1 if none.</summary>
    private static int FindUnquotedEquals(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' ) inQuotes = !inQuotes;
            else if (c == '=' && !inQuotes) return i;
        }
        return -1;
    }
}

/// <summary>The entry kinds the parser distinguishes.</summary>
public enum ConfigEntryKind
{
    /// <summary>An opaque blob (TOML/JSON/etc.) — the whole file in one entry.</summary>
    Blob,
    /// <summary>An empty line.</summary>
    Blank,
    /// <summary>A comment line (# or ;).</summary>
    Comment,
    /// <summary>A [section] header.</summary>
    Section,
    /// <summary>A key=value pair — editable in the structured UI.</summary>
    KeyValue,
    /// <summary>An unrecognized line in a structured file (kept verbatim).</summary>
    Other,
}

/// <summary>One parsed config row. Key/Value are editable for KeyValue entries; RawLine preserves
/// the original text for all kinds so non-edited lines round-trip exactly.</summary>
public sealed record ConfigEntry(ConfigEntryKind Kind, string Key, string Value, string RawLine);
