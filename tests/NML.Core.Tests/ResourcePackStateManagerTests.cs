using NML.Core.Modpacks;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ResourcePackStateManager"/> — reads/writes the enabled-packs list in
/// Minecraft's options.txt (a JSON array of "file/Name.zip" entries in a key=value line). Toggling
/// must be idempotent and preserve the rest of the file.
/// </summary>
public class ResourcePackStateManagerTests
{
    [Fact]
    public void ReadEnabled_Parses_Pack_Array()
    {
        string options = """
            version:3120
            resourcePacks:["file/Vanilla+.zip","file/Faithful.zip"]
            incompatibleResourcePacks:[]
            """;
        var enabled = ResourcePackStateManager.ReadEnabled(options);
        enabled.Should().Equal(new[] { "Vanilla+.zip", "Faithful.zip" });
    }

    [Fact]
    public void ReadEnabled_Returns_Empty_When_No_ResourcePacks_Line()
    {
        string options = "version:3120\nfov:0.5\n";
        ResourcePackStateManager.ReadEnabled(options).Should().BeEmpty();
    }

    [Fact]
    public void ReadEnabled_Returns_Empty_For_Empty_Array()
    {
        string options = "resourcePacks:[]\n";
        ResourcePackStateManager.ReadEnabled(options).Should().BeEmpty();
    }

    [Fact]
    public void WriteEnabled_Replaces_Existing_Line()
    {
        string options = """
            version:3120
            resourcePacks:["file/Old.zip"]
            fov:0.5
            """;
        string result = ResourcePackStateManager.WriteEnabled(options, new[] { "New.zip" });
        result.Should().Contain("resourcePacks:[\"file/New.zip\"]");
        result.Should().Contain("version:3120");
        result.Should().Contain("fov:0.5");
        result.Should().NotContain("Old.zip");
    }

    [Fact]
    public void WriteEnabled_Inserts_Line_When_Absent()
    {
        string options = "version:3120\nfov:0.5\n";
        string result = ResourcePackStateManager.WriteEnabled(options, new[] { "Pack.zip" });
        result.Should().Contain("resourcePacks:[\"file/Pack.zip\"]");
        result.Should().Contain("version:3120");
    }

    [Fact]
    public void Toggle_Enables_A_Disabled_Pack()
    {
        string options = "resourcePacks:[]\n";
        var (result, nowEnabled) = ResourcePackStateManager.Toggle(options, "MyPack.zip");
        nowEnabled.Should().BeTrue();
        ResourcePackStateManager.ReadEnabled(result).Should().Contain("MyPack.zip");
    }

    [Fact]
    public void Toggle_Disables_An_Enabled_Pack()
    {
        string options = "resourcePacks:[\"file/MyPack.zip\"]\n";
        var (result, nowEnabled) = ResourcePackStateManager.Toggle(options, "MyPack.zip");
        nowEnabled.Should().BeFalse();
        ResourcePackStateManager.ReadEnabled(result).Should().NotContain("MyPack.zip");
    }

    [Fact]
    public void Toggle_Is_Idempotent()
    {
        // Toggle twice → back to the original state.
        string options = "resourcePacks:[\"file/P.zip\"]\n";
        var (off, _) = ResourcePackStateManager.Toggle(options, "P.zip");
        var (on, _) = ResourcePackStateManager.Toggle(off, "P.zip");
        on.Should().Contain("file/P.zip");
    }

    [Fact]
    public void ReadEnabled_Tolerates_Malformed_JSON()
    {
        string options = "resourcePacks:not valid json\n";
        ResourcePackStateManager.ReadEnabled(options).Should().BeEmpty();
    }

    [Fact]
    public void WriteEnabled_Empty_List_Writes_Empty_Array()
    {
        string options = "resourcePacks:[\"file/Old.zip\"]\n";
        string result = ResourcePackStateManager.WriteEnabled(options, Array.Empty<string>());
        result.Should().Contain("resourcePacks:[]");
    }
}
