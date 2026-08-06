using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="WorldStatsCsvExporter"/> — converts world stats to CSV format.
/// </summary>
public class WorldStatsCsvExporterTests
{
    [Fact]
    public void ToCsv_Includes_Header_And_PlayTime()
    {
        var stats = new Dictionary<string, WorldStatEntry>
        {
            { "minecraft:mob_kills", new WorldStatEntry("Mob Kills", 42) },
            { "minecraft:deaths", new WorldStatEntry("Deaths", 3) },
        };
        var summary = new WorldStatsSummary(stats, 150, 5);

        string csv = WorldStatsCsvExporter.ToCsv(summary);
        csv.Should().StartWith("Stat,Value");
        csv.Should().Contain("Play Time (minutes),150");
        csv.Should().Contain("Mob Kills,42");
        csv.Should().Contain("Deaths,3");
    }

    [Fact]
    public void ToCsv_Empty_Stats_Still_Has_Header()
    {
        var csv = WorldStatsCsvExporter.ToCsv(new WorldStatsSummary());
        csv.Should().Be("Stat,Value\r\n");
    }

    [Fact]
    public void Escape_Quotes_Fields_With_Commas()
    {
        WorldStatsCsvExporter.Escape("hello").Should().Be("hello");
        WorldStatsCsvExporter.Escape("a,b").Should().Be("\"a,b\"");
        WorldStatsCsvExporter.Escape("say \"hi\"").Should().Be("\"say \"\"hi\"\"\"");
        WorldStatsCsvExporter.Escape("").Should().Be("");
    }

    [Fact]
    public void Export_Writes_File_To_Disk()
    {
        string worldDir = Path.Combine(Path.GetTempPath(), "nml-csv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(worldDir);
        string statsDir = Path.Combine(worldDir, "stats");
        Directory.CreateDirectory(statsDir);
        File.WriteAllText(Path.Combine(statsDir, "uuid.json"),
            """{"stats":{"minecraft:custom":{"minecraft:mob_kills":10,"minecraft:play_time":12000}}}""");
        string csvPath = Path.Combine(Path.GetTempPath(), "export-" + Guid.NewGuid().ToString("N")[..8] + ".csv");

        try
        {
            string result = WorldStatsCsvExporter.Export(worldDir, csvPath);
            result.Should().Be(csvPath);
            File.Exists(csvPath).Should().BeTrue();
            string content = File.ReadAllText(csvPath);
            content.Should().Contain("Stat,Value");
            content.Should().Contain("Mob Kills,10");
            content.Should().Contain("Play Time (minutes),10"); // 12000 ticks / 20 / 60 = 10 min
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
            Directory.Delete(worldDir, recursive: true);
        }
    }
}
