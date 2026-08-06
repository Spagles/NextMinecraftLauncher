using NML.Core.Modloaders;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ModUpdatePlanner"/> — the pure logic behind one-click "upgrade all": it
/// must select only the updatable mods with a usable jar URL, pair each with its target path, and
/// defensively skip mods with no update / no URL / a non-jar URL (so a broken LatestFileUrl can't
/// replace a mod with an HTML error page).
/// </summary>
public class ModUpdatePlannerTests
{
    private static InstalledModInfo Mod(string id, string file, bool update, string? url, string? latestVer = null) => new()
    {
        ModId = id,
        FileName = file,
        Version = "1.0.0",
        UpdateAvailable = update,
        LatestFileUrl = url,
        LatestVersion = latestVer,
    };

    [Fact]
    public void Plan_Includes_Only_Updatable_ModsWith_Jar_Urls()
    {
        var installed = new[]
        {
            Mod("sodium", "sodium-0.5.jar", update: true,  url: "https://cdn.modrinth.com/sodium-0.6.jar", latestVer: "sodium-0.6.jar"),
            Mod("iris",   "iris.jar",       update: true,  url: "https://cdn.modrinth.com/iris-1.7.jar",   latestVer: "iris-1.7.jar"),
            Mod("lithium","lithium.jar",    update: false, url: "https://cdn.modrinth.com/lithium.jar"),   // up-to-date → skip
            Mod("voice",  "voice.jar",      update: true,  url: null),                                     // no URL → skip
            Mod("broken", "broken.jar",     update: true,  url: "https://modrinth.com/not-found"),         // non-jar URL → skip
        };

        var plan = ModUpdatePlanner.Plan(installed, "/mc/mods");
        plan.Should().HaveCount(2);
        plan.Select(p => p.ModId).Should().BeEquivalentTo(new[] { "sodium", "iris" });
        plan.All(p => p.SourceUrl.EndsWith(".jar")).Should().BeTrue();
    }

    [Fact]
    public void Plan_TargetPath_Uses_LatestFileName_When_It_Looks_Like_A_File()
    {
        // LatestVersion is a real file name → the target path uses it (the old jar is replaced).
        var installed = new[]
        {
            Mod("sodium", "sodium-0.5.jar", update: true,
                url: "https://cdn.modrinth.com/sodium-0.6.jar", latestVer: "sodium-0.6.jar"),
        };
        var plan = ModUpdatePlanner.Plan(installed, "/mc/mods");
        plan.Single().TargetPath.Should().EndWith("sodium-0.6.jar");
        plan.Single().OldFileName.Should().Be("sodium-0.5.jar");
    }

    [Fact]
    public void Plan_TargetPath_Falls_Back_To_Original_FileName_When_LatestVersion_Is_A_Version()
    {
        // LatestVersion is a free-form version string ("1.2.3"), not a file name → keep the original
        // jar's name so the upgrade overwrites in place.
        var installed = new[]
        {
            Mod("sodium", "sodium.jar", update: true,
                url: "https://cdn.modrinth.com/data/sodium/sodium-0.6.jar", latestVer: "1.2.3"),
        };
        var plan = ModUpdatePlanner.Plan(installed, "/mc/mods");
        plan.Single().TargetPath.Should().EndWith("sodium.jar");
    }

    [Fact]
    public void Plan_Empty_When_Nothing_Updatable()
    {
        var installed = new[]
        {
            Mod("a", "a.jar", update: false, url: "https://x/a.jar"),
            Mod("b", "b.jar", update: true,  url: null),
        };
        ModUpdatePlanner.Plan(installed, "/mc/mods").Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://cdn.modrinth.com/x.jar", true)]
    [InlineData("https://cdn.modrinth.com/x.JAR", true)]  // case-insensitive
    [InlineData("https://cdn.modrinth.com/x.zip", true)]
    [InlineData("https://cdn.modrinth.com/x.jar?token=abc", true)] // query string stripped
    [InlineData("https://modrinth.com/not-found", false)]
    [InlineData("https://cdn.modrinth.com/x.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsModFileUrl_Only_Accepts_Jar_Or_Zip(string? url, bool expected)
        => ModUpdatePlanner.IsModFileUrl(url).Should().Be(expected);

    [Theory]
    [InlineData("sodium-0.6.jar", true)]
    [InlineData("data.zip", true)]
    [InlineData("1.2.3", false)]       // free-form version, not a file name
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeFileName_Requires_An_Archive_Extension(string? value, bool expected)
        => ModUpdatePlanner.LooksLikeFileName(value).Should().Be(expected);
}
