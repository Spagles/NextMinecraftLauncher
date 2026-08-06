using NML.Core.Theming;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ThemePreviewModel"/> — the pure derivation behind the Settings page's live
/// theme-preview card. The model must resolve the theme name to a light/dark surface, validate the
/// accent hex (falling back to a default so the swatch never goes transparent), and pick a readable
/// on-accent text color via perceptual luminance. These rules are unit-tested in isolation so the
/// preview never has to spin up the Avalonia UI thread.
/// </summary>
public class ThemePreviewModelTests
{
    [Theory]
    [InlineData("dark", false)]
    [InlineData("light", true)]
    [InlineData("system", false)] // treated as dark (the launcher default surface)
    [InlineData("", false)]
    public void IsLightTheme_Resolves_Theme_Name(string theme, bool expected)
    {
        var m = new ThemePreviewModel { Theme = theme };
        m.IsLightTheme.Should().Be(expected);
    }

    [Fact]
    public void PreviewBackground_And_Foreground_Flip_With_Theme()
    {
        var dark = new ThemePreviewModel { Theme = "dark" };
        dark.PreviewBackground.Should().Be(ThemePreviewModel.DarkBackground);
        dark.PreviewForeground.Should().Be(ThemePreviewModel.DarkForeground);

        var light = new ThemePreviewModel { Theme = "light" };
        light.PreviewBackground.Should().Be(ThemePreviewModel.LightBackground);
        light.PreviewForeground.Should().Be(ThemePreviewModel.LightForeground);
    }

    [Theory]
    [InlineData("#4fc3f7", true)]   // valid 6-digit
    [InlineData("#fff", true)]      // valid 3-digit shorthand
    [InlineData("#80ff0000", true)] // valid 8-digit ARGB
    [InlineData("4fc3f7", true)]    // valid without leading #
    [InlineData("#xyz", false)]     // non-hex digits
    [InlineData("", false)]
    [InlineData("not-a-color", false)]
    public void IsAccentValid_Recognizes_Hex_Formats(string accent, bool expected)
    {
        var m = new ThemePreviewModel { Accent = accent };
        m.IsAccentValid.Should().Be(expected);
    }

    [Fact]
    public void EffectiveAccent_Falls_Back_To_Default_On_Invalid_Hex()
    {
        // A broken accent must not render as transparent — the preview should show the default
        // blue so the user can still see *something* while they fix the typo.
        var m = new ThemePreviewModel { Accent = "garbage" };
        m.EffectiveAccent.Should().Be(ThemePreviewModel.DefaultAccent);
    }

    [Fact]
    public void EffectiveAccent_Passes_Through_Valid_Hex()
    {
        var m = new ThemePreviewModel { Accent = "#ff7043" };
        m.EffectiveAccent.Should().Be("#ff7043");
    }

    [Theory]
    // Dark accents read best with white text; light accents with black. The cutoff is a
    // perceptual-luminance threshold of 0.5 (Rec. 601 weights, normalized to 0–1).
    [InlineData("#000000", true)]   // near-black → dark → white text
    [InlineData("#1565c0", true)]   // deep blue → dark → white text
    [InlineData("#4fc3f7", false)]  // launcher default blue is lightish (lum ≈ 0.65) → black text
    [InlineData("#ffffff", false)]  // white → light → black text
    [InlineData("#ffe082", false)]  // light amber → light → black text
    public void IsAccentDark_Picks_Readable_OnAccent_Text(string accent, bool expectDark)
    {
        ThemePreviewModel.IsAccentDark(accent).Should().Be(expectDark);
        var m = new ThemePreviewModel { Accent = accent };
        m.AccentOnColor.Should().Be(expectDark ? "#ffffff" : "#111111");
    }

    [Fact]
    public void SampleText_Describes_Theme_And_Accent()
    {
        var m = new ThemePreviewModel { Theme = "light", Accent = "#ff7043" };
        m.SampleText.Should().Contain("Light");
        m.SampleText.Should().Contain("#ff7043");
    }

    [Fact]
    public void TryParseHex_Normalizes_Shorthand_Forms()
    {
        // 3-digit shorthand expands to the 6-digit equivalent.
        ThemePreviewModel.TryParseHex("#f0f", out byte r, out byte g, out byte b).Should().BeTrue();
        (r, g, b).Should().Be((0xff, 0x00, 0xff));
        // 4-digit ARGB shorthand drops alpha and expands RGB.
        ThemePreviewModel.TryParseHex("#8f00", out r, out g, out b).Should().BeTrue();
        (r, g, b).Should().Be((0xff, 0x00, 0x00));
    }
}
