using NML.Core.Modloaders;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="OptiFineInstaller.ParseBmclVersions"/> — the pure JSON parser behind the
/// OptiFine version list (fetched from the BMCLAPI mirror). This is the only OptiFine code path
/// that's pure enough to unit-test off-network; the install path shells out to Java.
/// </summary>
public class OptiFineInstallerTests
{
    [Fact]
    public void Parses_BMCL_Version_List()
    {
        // Realistic BMCLAPI /optifine/{mc} response shape: array of { type, patch }.
        string json = """
        [
          { "type": "C6", "patch": "" },
          { "type": "C5", "patch": "beta1" }
        ]
        """;
        var versions = OptiFineInstaller.ParseBmclVersions(json, "1.20.1");
        versions.Should().HaveCount(2);
        versions[0].GameVersion.Should().Be("1.20.1");
        versions[0].Type.Should().Be("C6");
        versions[0].Patch.Should().Be("");
        versions[0].Display.Should().Contain("1.20.1").And.Contain("C6");
        versions[1].Patch.Should().Be("beta1");
        versions[1].Display.Should().Contain("beta1");
    }

    [Fact]
    public void Skips_Entries_With_Empty_Type()
    {
        string json = """
        [
          { "type": "", "patch": "" },
          { "type": "I7", "patch": "" }
        ]
        """;
        var versions = OptiFineInstaller.ParseBmclVersions(json, "1.19.2");
        versions.Should().HaveCount(1);
        versions[0].Type.Should().Be("I7");
    }

    [Fact]
    public void Returns_Empty_On_Malformed_Json()
    {
        OptiFineInstaller.ParseBmclVersions("not json", "1.20.1").Should().BeEmpty();
        OptiFineInstaller.ParseBmclVersions("", "1.20.1").Should().BeEmpty();
    }

    [Fact]
    public void Returns_Empty_On_Empty_Array()
        => OptiFineInstaller.ParseBmclVersions("[]", "1.20.1").Should().BeEmpty();

    [Fact]
    public void Missing_Patch_Field_Defaults_To_Empty()
    {
        // Entries without a 'patch' field shouldn't crash; patch falls back to "".
        string json = """
        [ { "type": "E1" } ]
        """;
        var versions = OptiFineInstaller.ParseBmclVersions(json, "1.18.2");
        versions.Should().HaveCount(1);
        versions[0].Patch.Should().Be("");
    }
}
