using NML.Core.Theming;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="CustomCssManager"/> — the validate/load/persist surface behind the custom
/// CSS import. Validation rejects empty/binary/oversized input and strips a BOM; persistence
/// round-trips a stylesheet and clears cleanly. Pure file operations, no UI.
/// </summary>
public class CustomCssManagerTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-css-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Rejects_Empty_Or_Whitespace(string? raw)
        => CustomCssManager.Validate(raw).Should().BeNull();

    [Fact]
    public void Validate_Strips_Leading_BOM()
    {
        // A pasted CSS with a UTF-8 BOM must have it removed before persisting.
        string bom = "\uFEFF";
        CustomCssManager.Validate(bom + "Button { color: red; }").Should().Be("Button { color: red; }");
    }

    [Fact]
    public void Validate_Presves_Normal_CSS()
    {
        string css = "/* comment */\nButton.primary { background: #2f5fcc; }\n";
        CustomCssManager.Validate(css).Should().Be(css);
    }

    [Fact]
    public void Validate_Rejects_Input_With_NUL()
    {
        // A binary blob (e.g. a jar mistakenly pasted) contains a NUL — reject it.
        CustomCssManager.Validate("Button \0 { }").Should().BeNull();
    }

    [Fact]
    public void Validate_Rejects_Oversized_Input()
    {
        // Build input larger than the 1 MiB cap.
        string big = new string('x', CustomCssManager.MaxBytes + 1);
        CustomCssManager.Validate(big).Should().BeNull();
        // Exactly-at-cap is accepted (boundary).
        CustomCssManager.Validate(new string('x', CustomCssManager.MaxBytes)).Should().NotBeNull();
    }

    [Fact]
    public void IsValid_Mirrors_Validate()
    {
        CustomCssManager.IsValid("Button { }").Should().BeTrue();
        CustomCssManager.IsValid("").Should().BeFalse();
        CustomCssManager.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void Save_Load_RoundTrips_Validated_CSS()
    {
        string dir = TempDir();
        try
        {
            var mgr = new CustomCssManager(dir);
            string css = "Button { color: blue; }";
            mgr.Save(css).Should().BeTrue();
            mgr.HasCustomCss().Should().BeTrue();
            mgr.Load().Should().Be(css);
            mgr.FilePath.Should().EndWith(CustomCssManager.FileName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_Deletes_File_When_Input_Rejected()
    {
        // Saving invalid/empty input clears the persisted stylesheet (no stale file left behind).
        string dir = TempDir();
        try
        {
            var mgr = new CustomCssManager(dir);
            mgr.Save("Button { }");
            File.Exists(mgr.FilePath).Should().BeTrue();
            mgr.Save("").Should().BeFalse();   // empty → rejected → file removed
            File.Exists(mgr.FilePath).Should().BeFalse();
            mgr.HasCustomCss().Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_Returns_Null_When_No_File()
    {
        string dir = TempDir();
        try
        {
            var mgr = new CustomCssManager(dir);
            mgr.Load().Should().BeNull();
            mgr.HasCustomCss().Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Clear_Removes_Persisted_File()
    {
        string dir = TempDir();
        try
        {
            var mgr = new CustomCssManager(dir);
            mgr.Save("Button { }");
            File.Exists(mgr.FilePath).Should().BeTrue();
            mgr.Clear();
            File.Exists(mgr.FilePath).Should().BeFalse();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
