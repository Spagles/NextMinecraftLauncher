using System.IO;
using System.Text;

namespace NML.Core.Game;

/// <summary>
/// Exports a world's play statistics to CSV format so the user can share or analyze them in a
/// spreadsheet. Pure + unit-tested. Produces a standard "Label,Value" CSV with a header row.
/// </summary>
public static class WorldStatsCsvExporter
{
    /// <summary>Convert a WorldStatsSummary to CSV text.</summary>
    public static string ToCsv(WorldStatsSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Stat,Value");
        if (summary.PlayTimeMinutes > 0)
            sb.AppendLine($"Play Time (minutes),{summary.PlayTimeMinutes}");
        foreach (var entry in summary.TrackedStats.Values)
            sb.AppendLine($"{Escape(entry.Label)},{entry.Value}");
        return sb.ToString();
    }

    /// <summary>Export a world's stats to a CSV file on disk. Returns the file path.</summary>
    public static string Export(string worldDir, string outputPath)
    {
        var summary = WorldStatsReader.Read(worldDir);
        string csv = ToCsv(summary);
        File.WriteAllText(outputPath, csv);
        return outputPath;
    }

    /// <summary>Escape a CSV field: wrap in quotes if it contains a comma, quote, or newline.</summary>
    public static string Escape(string field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
