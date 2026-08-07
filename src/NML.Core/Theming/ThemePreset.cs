using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.Core.Theming;

/// <summary>
/// A portable, shareable theme snapshot: the variant (dark/light/system), the accent color, the
/// font scale, and the custom CSS. Exported/imported as a single JSON file so users can back up a
/// tuned look or share it (HMCL-style theme files). Pure + round-trippable.
/// </summary>
public sealed record ThemePreset
{
    /// <summary>A magic header so we can distinguish a theme preset from arbitrary JSON on import.</summary>
    public const string Magic = "nml-theme-preset";

    /// <summary>Identifies the file format + version. <see cref="Magic"/> when produced by Export;
    /// empty by default so a foreign JSON (which won't set this) is rejected on import.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>A friendly name for the preset (shown in the import dialog).</summary>
    public string Name { get; init; } = "My Theme";

    /// <summary>Theme variant: "dark", "light", or "system".</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Accent color hex (e.g. "#4fc3f7").</summary>
    public string Accent { get; init; } = ThemePreviewModel.DefaultAccent;

    /// <summary>Font size scale (0.8–1.3).</summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>Custom CSS/Avalonia style text (may be empty).</summary>
    public string CustomCss { get; init; } = string.Empty;
}

/// <summary>
/// Serializes <see cref="ThemePreset"/> to/from a JSON file, with validation on import so a malformed
/// or foreign file is rejected with a clear error rather than silently corrupting the user's theme.
/// </summary>
public static class ThemePresetSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Write a preset to <paramref name="path"/> as pretty-printed JSON. Stamps the
    /// <see cref="ThemePreset.Magic"/> type header so import can validate the file origin.</summary>
    public static void Export(ThemePreset preset, string path)
    {
        if (preset is null) throw new ArgumentNullException(nameof(preset));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(preset with { Type = ThemePreset.Magic }, Options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Read + validate a preset from <paramref name="path"/>. Throws <see cref="InvalidDataException"/>
    /// when the file is missing, not valid JSON, or lacks the <see cref="ThemePreset.Magic"/> header
    /// (so a random .json can't masquerade as a theme and overwrite settings).
    /// </summary>
    public static ThemePreset Import(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Theme preset file not found.", path);

        string json = File.ReadAllText(path);
        ThemePreset? preset;
        try { preset = JsonSerializer.Deserialize<ThemePreset>(json, Options); }
        catch (JsonException ex) { throw new InvalidDataException($"File is not valid JSON: {ex.Message}", ex); }

        if (preset is null)
            throw new InvalidDataException("Theme preset deserialized to null.");
        if (!string.Equals(preset.Type, ThemePreset.Magic, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Not a theme preset (expected type '{ThemePreset.Magic}', got '{preset.Type}').");

        // Normalize/clamp fields so an out-of-range imported value can't break the UI.
        return preset with
        {
            FontScale = Math.Clamp(preset.FontScale, 0.8, 1.3),
            Accent = string.IsNullOrWhiteSpace(preset.Accent) ? ThemePreviewModel.DefaultAccent : preset.Accent,
            Theme = string.IsNullOrWhiteSpace(preset.Theme) ? "dark" : preset.Theme.ToLowerInvariant(),
        };
    }
}
