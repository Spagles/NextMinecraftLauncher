using System.IO;
using NML.Core.Theming;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ThemePresetSerializer"/> — the export/import round trip for theme presets.
/// Guards against: missing files, malformed JSON, foreign files lacking the magic header, and
/// out-of-range imported values that could break the UI.
/// </summary>
public class ThemePresetSerializerTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), "nml-theme-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    private static ThemePreset Sample() => new()
    {
        Name = "Sunset",
        Theme = "light",
        Accent = "#ff7043",
        FontScale = 1.1,
        CustomCss = "Button.small { background: red; }",
    };

    [Fact]
    public void Export_Import_Round_Trips_All_Fields()
    {
        string path = TempPath();
        try
        {
            var preset = Sample();
            ThemePresetSerializer.Export(preset, path);
            var loaded = ThemePresetSerializer.Import(path);

            loaded.Name.Should().Be("Sunset");
            loaded.Theme.Should().Be("light");
            loaded.Accent.Should().Be("#ff7043");
            loaded.FontScale.Should().Be(1.1);
            loaded.CustomCss.Should().Be("Button.small { background: red; }");
            loaded.Type.Should().Be(ThemePreset.Magic);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Export_Creates_Parent_Directory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-theme-dir-" + Guid.NewGuid().ToString("N")[..8]);
        string path = Path.Combine(dir, "nested", "theme.json");
        try
        {
            ThemePresetSerializer.Export(Sample(), path);
            File.Exists(path).Should().BeTrue("export must create missing parent directories");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Import_Throws_On_Missing_File()
    {
        var act = () => ThemePresetSerializer.Import(Path.Combine(Path.GetTempPath(), "nml-nope-" + Guid.NewGuid() + ".json"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Import_Throws_On_Malformed_Json()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, "not valid json {{{");
            var act = () => ThemePresetSerializer.Import(path);
            act.Should().Throw<InvalidDataException>();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Import_Throws_On_Foreign_File_Without_Magic()
    {
        // A random JSON file without the nml-theme-preset type header must be rejected, so importing
        // an unrelated .json can't silently overwrite the user's theme.
        string path = TempPath();
        try
        {
            File.WriteAllText(path, """{"name":"something else","theme":"dark"}""");
            var act = () => ThemePresetSerializer.Import(path);
            act.Should().Throw<InvalidDataException>().WithMessage("*nml-theme-preset*");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Import_Clamps_FontScale_Into_Range()
    {
        // An out-of-range FontScale (e.g. 5.0) must be clamped so it can't break the UI.
        string path = TempPath();
        try
        {
            var extreme = Sample() with { FontScale = 5.0 };
            ThemePresetSerializer.Export(extreme, path);
            var loaded = ThemePresetSerializer.Import(path);
            loaded.FontScale.Should().Be(1.3, "FontScale must clamp to the max (1.3)");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Import_Normalizes_Empty_Accent_And_Theme()
    {
        string path = TempPath();
        try
        {
            var blank = Sample() with { Accent = "", Theme = "" };
            ThemePresetSerializer.Export(blank, path);
            var loaded = ThemePresetSerializer.Import(path);
            loaded.Accent.Should().Be(ThemePreviewModel.DefaultAccent, "empty accent falls back to default");
            loaded.Theme.Should().Be("dark", "empty theme normalizes to dark");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Imported_Preset_Applies_Via_Preview_Model()
    {
        // A preset should drive a ThemePreviewModel correctly (the bridge to the live UI).
        string path = TempPath();
        try
        {
            ThemePresetSerializer.Export(Sample(), path);
            var loaded = ThemePresetSerializer.Import(path);
            var preview = new ThemePreviewModel { Theme = loaded.Theme, Accent = loaded.Accent };

            preview.IsLightTheme.Should().BeTrue();
            preview.EffectiveAccent.Should().Be("#ff7043");
            preview.IsAccentValid.Should().BeTrue();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
