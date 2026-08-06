using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NML.Core.Modpacks;

/// <summary>
/// Manages which resource packs are enabled in a Minecraft instance's <c>options.txt</c>. Minecraft
/// stores this as <c>resourcePacks:["file/MyPack.zip"]</c> (a JSON array in a key=value line). This
/// reader/writer toggles entries on or off, so the launcher can flip a pack without launching the
/// game. Pure + unit-tested against synthetic options.txt content.
/// <para>
/// The "file/" prefix is Minecraft's convention — packs in the resourcepacks/ folder are referenced
/// as "file/Name.zip". We add/remove it transparently so callers just pass the pack file name.
/// </para>
/// </summary>
public static class ResourcePackStateManager
{
    /// <summary>Parse the enabled-pack names from an <c>options.txt</c> body. Returns the bare file
    /// names (without the "file/" prefix).</summary>
    public static IReadOnlyList<string> ReadEnabled(string optionsTxt)
    {
        if (string.IsNullOrEmpty(optionsTxt)) return Array.Empty<string>();
        foreach (string line in optionsTxt.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("resourcePacks:", System.StringComparison.OrdinalIgnoreCase))
                continue;
            string jsonPart = trimmed["resourcePacks:".Length..].Trim();
            return ParsePackArray(jsonPart);
        }
        return Array.Empty<string>();
    }

    /// <summary>Write the enabled-pack list back into an <c>options.txt</c> body, replacing the
    /// existing resourcePacks line (or inserting one if absent).</summary>
    public static string WriteEnabled(string optionsTxt, IEnumerable<string> enabledPackNames)
    {
        var names = enabledPackNames.ToList();
        var entries = names.Select(n => $"\"file/{n}\"");
        string newLine = $"resourcePacks:[{string.Join(",", entries)}]";

        var lines = (optionsTxt ?? string.Empty).Split('\n').ToList();
        bool replaced = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith("resourcePacks:", System.StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = newLine;
                replaced = true;
                break;
            }
        }
        if (!replaced) lines.Add(newLine);
        return string.Join('\n', lines);
    }

    /// <summary>Toggle a single pack's enabled state in an options.txt body.</summary>
    public static (string OptionsTxt, bool NowEnabled) Toggle(string optionsTxt, string packFileName)
    {
        var enabled = ReadEnabled(optionsTxt).ToList();
        if (enabled.Remove(packFileName))
            return (WriteEnabled(optionsTxt, enabled), false); // was enabled → now disabled
        enabled.Add(packFileName);
        return (WriteEnabled(optionsTxt, enabled), true); // was disabled → now enabled
    }

    /// <summary>Parse a Minecraft-format JSON array like <c>["file/A.zip","file/B.zip"]</c> into
    /// bare file names (stripping the "file/" prefix). Tolerant of malformed JSON.</summary>
    private static IReadOnlyList<string> ParsePackArray(string json)
    {
        try
        {
            // Minecraft's options.txt uses a non-standard JSON (no quoted keys in some versions),
            // but resourcePacks is always a valid JSON array of strings.
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            if (arr is null) return Array.Empty<string>();
            return arr
                .Select(e => e.StartsWith("file/", System.StringComparison.Ordinal) ? e["file/".Length..] : e)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
