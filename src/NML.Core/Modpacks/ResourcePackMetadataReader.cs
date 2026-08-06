using System.IO.Compression;
using System.Text.Json;

namespace NML.Core.Modpacks;

/// <summary>
/// Reads a Minecraft resource pack's <c>pack.mcmeta</c> (description + pack_format) and its
/// <c>pack.png</c> icon, so the launcher can show a preview card with the pack's name + description +
/// icon instead of just a filename. Pure + unit-tested against synthetic zips.
/// </summary>
public sealed class ResourcePackMetadata
{
    /// <summary>The pack's description from pack.mcmeta (may include §-color codes).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The pack_format integer (version compatibility indicator).</summary>
    public int PackFormat { get; init; }

    /// <summary>Absolute path to the pack.png icon, or null when none.</summary>
    public string? IconPath { get; init; }
}

/// <summary>
/// Pure parser + zip reader for resource-pack metadata. No UI dependency.
/// </summary>
public static class ResourcePackMetadataReader
{
    /// <summary>Parse a <c>pack.mcmeta</c> body into description + pack_format. Tolerant of missing
    /// fields or malformed JSON (returns defaults rather than throwing).</summary>
    public static (string Description, int PackFormat) ParsePackMcMeta(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (string.Empty, 0);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string desc = string.Empty;
            int format = 0;

            // pack.mcmeta: { "pack": { "description": "...", "pack_format": 15 } }
            if (root.TryGetProperty("pack", out var pack))
            {
                if (pack.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                    desc = d.GetString() ?? string.Empty;
                else if (pack.TryGetProperty("description", out var dObj) && dObj.ValueKind == JsonValueKind.Object)
                {
                    // Some packs use a chat-component object: { "text": "..." }
                    if (dObj.TryGetProperty("text", out var t))
                        desc = t.GetString() ?? string.Empty;
                }
                if (pack.TryGetProperty("pack_format", out var pf) && pf.ValueKind == JsonValueKind.Number)
                    format = pf.GetInt32();
            }
            return (desc, format);
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    /// <summary>Read metadata from a resource-pack .zip on disk. Returns null when the file doesn't
    /// exist or isn't a readable zip.</summary>
    public static ResourcePackMetadata? Read(string zipPath)
    {
        if (!File.Exists(zipPath)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var mcmetaEntry = archive.GetEntry("pack.mcmeta");
            string description = string.Empty;
            int format = 0;
            if (mcmetaEntry is not null)
            {
                using var s = mcmetaEntry.Open();
                using var reader = new StreamReader(s);
                (description, format) = ParsePackMcMeta(reader.ReadToEnd());
            }
            bool hasIcon = archive.GetEntry("pack.png") is not null;
            return new ResourcePackMetadata
            {
                Description = description,
                PackFormat = format,
                IconPath = hasIcon ? zipPath : null,
            };
        }
        catch
        {
            return null;
        }
    }
}
