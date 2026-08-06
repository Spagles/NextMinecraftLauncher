using System.Globalization;

namespace NML.Core.Theming;

/// <summary>
/// Pure, UI-independent derivation of the theme-preview surface from the current theme + accent
/// settings. Lives in Core so the color/contrast math is unit-testable without spinning up the
/// Avalonia UI thread — the SettingsPage view model binds directly to an instance of this.
/// <para>
/// The same model drives both the live preview card and any future "is this accent readable on
/// this theme" validation, so the rules are centralized rather than scattered through XAML.
/// </para>
/// </summary>
public sealed class ThemePreviewModel
{
    /// <summary>Default accent used when none is set or the hex is invalid (matches the app default).</summary>
    public const string DefaultAccent = "#4fc3f7";

    /// <summary>Background hex for the dark preview card.</summary>
    public const string DarkBackground = "#1a1a20";
    /// <summary>Background hex for the light preview card.</summary>
    public const string LightBackground = "#f5f5f7";
    /// <summary>Foreground (text) hex for the dark preview card.</summary>
    public const string DarkForeground = "#e0e0e0";
    /// <summary>Foreground (text) hex for the light preview card.</summary>
    public const string LightForeground = "#222222";

    /// <summary>Theme name: "dark", "light", or "system".</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Accent color hex (e.g. "#4fc3f7"). May be empty or invalid.</summary>
    public string Accent { get; init; } = DefaultAccent;

    /// <summary>True when the resolved theme is light (drives the preview background/foreground).</summary>
    public bool IsLightTheme => string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase);

    /// <summary>Preview card background hex (light or dark surface).</summary>
    public string PreviewBackground => IsLightTheme ? LightBackground : DarkBackground;

    /// <summary>Preview card foreground hex (readable on the background).</summary>
    public string PreviewForeground => IsLightTheme ? LightForeground : DarkForeground;

    /// <summary>True when <see cref="Accent"/> parses to a valid ARGB/RGB hex color.</summary>
    public bool IsAccentValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Accent)) return false;
            return TryParseHex(Accent, out _, out _, out _);
        }
    }

    /// <summary>The accent to actually render — falls back to <see cref="DefaultAccent"/> when the
    /// user-typed hex is invalid, so the preview never shows a broken (transparent) swatch.</summary>
    public string EffectiveAccent => IsAccentValid ? Accent! : DefaultAccent;

    /// <summary>
    /// Recommended contrast hint: whether white or black text reads better on a fill of the accent
    /// color. Computed via the W3C relative-luminance threshold (the accent is "dark" → use white).
    /// Used to color text drawn on top of an accent-colored button.
    /// </summary>
    public string AccentOnColor => IsAccentValid && IsAccentDark(Accent!) ? "#ffffff" : "#111111";

    /// <summary>A human-readable sample describing the current selection (for the preview header).</summary>
    public string SampleText => $"{(IsLightTheme ? "Light" : "Dark")} · {EffectiveAccent}";

    /// <summary>
    /// Parse a hex color (#RGB, #RRGGBB, #ARGB, or #AARRGGBB, with or without leading #) into its
    /// 8-bit RGB components. Tolerant of the common Minecraft/UI hex variants.
    /// </summary>
    public static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string h = hex.Trim();
        if (h.StartsWith('#')) h = h[1..];
        // Normalize 3-digit (#RGB) and 4-digit (#ARGB) shorthand to 6/8 digits.
        if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        else if (h.Length == 4) h = $"{h[1]}{h[1]}{h[2]}{h[2]}{h[3]}{h[3]}"; // drop alpha, expand RGB
        if (h.Length != 6 && h.Length != 8) return false;
        if (!byte.TryParse(h.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!byte.TryParse(h.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return false;
        if (!byte.TryParse(h.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return false;
        return true;
    }

    /// <summary>W3C-style relative-luminance check: an accent with luminance below 0.5 reads best
    /// with white text on top of it.</summary>
    public static bool IsAccentDark(string hex)
    {
        if (!TryParseHex(hex, out byte r, out byte g, out byte b)) return false;
        // Perceptual luminance (Rec. 601 weights, normalized 0–255 → 0–1).
        double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return lum < 0.5;
    }
}
