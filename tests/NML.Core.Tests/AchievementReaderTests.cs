using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="AchievementReader"/> — reads a world's advancement progress from its
/// advancements/*.json files, counting done vs total and excluding recipe unlocks.
/// </summary>
public class AchievementReaderTests
{
    private static string MakeWorld(params (string FileName, string Json)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-ach-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string advDir = Path.Combine(dir, "advancements");
        Directory.CreateDirectory(advDir);
        foreach (var (name, json) in files)
            File.WriteAllText(Path.Combine(advDir, name), json);
        return dir;
    }

    [Fact]
    public void Read_Counts_Done_And_Total()
    {
        string dir = MakeWorld(("test.json", """
            {
              "minecraft:story/mine_diamond": { "criteria": {}, "done": true },
              "minecraft:story/iron_tools": { "criteria": {}, "done": false },
              "minecraft:recipes/misc/diamond_sword": { "criteria": {}, "done": true }
            }
            """));
        try
        {
            var summary = AchievementReader.Read(dir);
            summary.TotalAdvancements.Should().Be(2); // recipe excluded
            summary.CompletedAdvancements.Should().Be(1);
            summary.CompletedIds.Should().Contain("minecraft:story/mine_diamond");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Read_Handles_Multiple_Files()
    {
        string dir = MakeWorld(
            ("a.json", """{"minecraft:story/stone_age": {"done": true}}"""),
            ("b.json", """{"minecraft:nether/find_fortress": {"done": true}, "minecraft:end/dragon_egg": {"done": false}}""")
        );
        try
        {
            var summary = AchievementReader.Read(dir);
            summary.TotalAdvancements.Should().Be(3);
            summary.CompletedAdvancements.Should().Be(2);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Read_Returns_Empty_When_No_Advancements_Dir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-noach-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var summary = AchievementReader.Read(dir);
            summary.TotalAdvancements.Should().Be(0);
            summary.CompletedAdvancements.Should().Be(0);
            summary.Display.Should().Be("0 / 0 (0.0%)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void PercentComplete_Calculated_Correctly()
    {
        var s = new AchievementSummary(10, 3, new[] { "a", "b", "c" });
        s.PercentComplete.Should().Be(30.0);
        s.Display.Should().Be("3 / 10 (30.0%)");
    }

    [Fact]
    public void Read_Skips_Unparseable_Files()
    {
        string dir = MakeWorld(
            ("good.json", """{"minecraft:story/root": {"done": true}}"""),
            ("bad.json", "not json at all")
        );
        try
        {
            var summary = AchievementReader.Read(dir);
            summary.TotalAdvancements.Should().Be(1); // bad file skipped
            summary.CompletedAdvancements.Should().Be(1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
