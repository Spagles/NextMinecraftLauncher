using NML.Core.Modloaders;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ModloaderCompatibilityChecker"/> — validates that a modloader version
/// string is compatible with a given Minecraft game version, catching mismatches before install.
/// </summary>
public class ModloaderCompatibilityCheckerTests
{
    // --- Vanilla (always OK) ---

    [Fact]
    public void Vanilla_Is_Always_Compatible()
    {
        ModloaderCompatibilityChecker.Check("vanilla", "", "1.20.1").Ok.Should().BeTrue();
        ModloaderCompatibilityChecker.Check("", "", "1.20.1").Ok.Should().BeTrue();
    }

    // --- Forge (version-bound) ---

    [Fact]
    public void Forge_Matching_GameVersion_Is_Compatible()
    {
        var r = ModloaderCompatibilityChecker.Check("forge", "forge-1.20.1-47.2.0", "1.20.1");
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void Forge_Mismatched_GameVersion_Is_Incompatible()
    {
        var r = ModloaderCompatibilityChecker.Check("forge", "forge-1.19.2-43.2.0", "1.20.1");
        r.Ok.Should().BeFalse();
        r.Reason.Should().Be(ModloaderCompatibilityReason.VersionMismatch);
    }

    // --- NeoForge (version-key derived from MC version) ---

    [Fact]
    public void NeoForge_Matching_Key_Is_Compatible()
    {
        var r = ModloaderCompatibilityChecker.Check("neoforge", "20.1-47", "1.20.1");
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void NeoForge_Mismatched_Key_Is_Incompatible()
    {
        var r = ModloaderCompatibilityChecker.Check("neoforge", "19.2-43", "1.20.1");
        r.Ok.Should().BeFalse();
    }

    [Theory]
    [InlineData("1.20.1", "20.1")]
    [InlineData("1.19.2", "19.2")]
    [InlineData("1.18", "18")]
    public void DeriveNeoForgeKey_Strips_Leading_1(string gameVersion, string expected)
    {
        ModloaderCompatibilityChecker.DeriveNeoForgeKey(gameVersion).Should().Be(expected);
    }

    // --- Fabric/Quilt (universal or version-embedded) ---

    [Fact]
    public void Fabric_Universal_Loader_Is_Compatible()
    {
        // A Fabric loader version with no embedded game version ("0.15.7") is universal → OK on any game version.
        var r = ModloaderCompatibilityChecker.Check("fabric", "0.15.7", "1.20.1");
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void Fabric_Version_Bound_Matching_Is_Compatible()
    {
        // "0.15.7-1.20.1" embeds the game version and it matches → OK.
        var r = ModloaderCompatibilityChecker.Check("fabric", "0.15.7-1.20.1", "1.20.1");
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void Fabric_Version_Bound_Mismatch_Is_Incompatible()
    {
        // "0.15.7-1.19.4" embeds a game version that doesn't match → incompatible.
        var r = ModloaderCompatibilityChecker.Check("fabric", "0.15.7-1.19.4", "1.20.1");
        r.Ok.Should().BeFalse();
    }

    // --- OptiFine (version-bound) ---

    [Fact]
    public void OptiFine_Matching_Is_Compatible()
    {
        var r = ModloaderCompatibilityChecker.Check("optifine", "OptiFine_1.20.1_HD_U_I6", "1.20.1");
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public void OptiFine_Mismatched_Is_Incompatible()
    {
        var r = ModloaderCompatibilityChecker.Check("optifine", "OptiFine_1.19.4_HD_U_I6", "1.20.1");
        r.Ok.Should().BeFalse();
    }

    // --- Edge cases ---

    [Fact]
    public void Missing_GameVersion_Is_Incompatible()
    {
        var r = ModloaderCompatibilityChecker.Check("forge", "forge-1.20.1-47", "");
        r.Ok.Should().BeFalse();
        r.Reason.Should().Be(ModloaderCompatibilityReason.MissingGameVersion);
    }

    [Fact]
    public void Unknown_Modloader_Is_Assumed_Compatible()
    {
        ModloaderCompatibilityChecker.Check("customloader", "1.0", "1.20.1").Ok.Should().BeTrue();
    }
}
