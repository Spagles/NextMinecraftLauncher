using NML.Core.Modloaders;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="LiteLoaderInstaller.ParseList"/> — the pure BMCLAPI JSON parser behind the
/// LiteLoader version list — and the LiteLoader compatibility check (≤ 1.12.2 only).
/// </summary>
public class LiteLoaderInstallerTests
{
    // Realistic BMCLAPI /liteloader/list entry shapes (trimmed for the test).
    private const string SampleJson = """
    [
      {"mcversion":"1.12.2","build":{"tweakClass":"com.mumfrey.liteloader.launch.LiteLoaderTweaker","file":"liteloader-1.12.2-SNAPSHOT.jar","version":"1.12.2-SNAPSHOT"}},
      {"mcversion":"1.7.10","build":{"tweakClass":"com.mumfrey.liteloader.launch.LiteLoaderTweaker","file":"liteloader-1.7.10.jar","version":"1.7.10_04"}}
    ]
    """;

    [Fact]
    public void Parses_Version_List()
    {
        var versions = LiteLoaderInstaller.ParseList(SampleJson);
        versions.Should().HaveCount(2);
        versions[0].GameVersion.Should().Be("1.12.2");
        versions[0].File.Should().Be("liteloader-1.12.2-SNAPSHOT.jar");
        versions[0].TweakClass.Should().Contain("LiteLoaderTweaker");
        versions[0].Display.Should().Contain("1.12.2");
        versions[1].Version.Should().Be("1.7.10_04");
    }

    [Fact]
    public void Skips_Entries_Without_File()
    {
        string json = """
        [
          {"mcversion":"1.10.2","build":{"file":""}},
          {"mcversion":"1.8","build":{"file":"liteloader-1.8.jar","version":"1.8"}}
        ]
        """;
        var versions = LiteLoaderInstaller.ParseList(json);
        versions.Should().HaveCount(1);
        versions[0].GameVersion.Should().Be("1.8");
    }

    [Fact]
    public void Skips_Entries_Without_McVersion()
    {
        string json = """
        [
          {"mcversion":"","build":{"file":"x.jar"}},
          {"mcversion":"1.9.4","build":{"file":"liteloader-1.9.4.jar"}}
        ]
        """;
        LiteLoaderInstaller.ParseList(json).Should().HaveCount(1);
    }

    [Fact]
    public void Defaults_TweakClass_When_Missing()
    {
        string json = """[{"mcversion":"1.11.2","build":{"file":"liteloader-1.11.2.jar","version":"1.11.2"}}]""";
        var v = LiteLoaderInstaller.ParseList(json).Single();
        v.TweakClass.Should().Be("com.mumfrey.liteloader.launch.LiteLoaderTweaker");
    }

    [Fact]
    public void Returns_Empty_On_Malformed_Json()
    {
        LiteLoaderInstaller.ParseList("not json").Should().BeEmpty();
        LiteLoaderInstaller.ParseList("").Should().BeEmpty();
    }

    // ===== Compatibility checker: LiteLoader is ≤ 1.12.2 only =====

    [Theory]
    [InlineData("1.12.2", true)]
    [InlineData("1.7.10", true)]
    [InlineData("1.10.2", true)]
    [InlineData("1.13", false)]   // first unsupported
    [InlineData("1.20.1", false)]
    [InlineData("2.0.0", false)]
    public void LiteLoader_Compat_Rejects_Post_1_12_2(string gameVersion, bool expectedOk)
    {
        var result = ModloaderCompatibilityChecker.Check("liteloader", gameVersion, gameVersion);
        result.Ok.Should().Be(expectedOk);
    }

    [Fact]
    public void LiteLoader_Compat_Requires_Game_Version()
    {
        var result = ModloaderCompatibilityChecker.Check("liteloader", "1.12.2", "");
        result.Ok.Should().BeFalse();
    }
}
