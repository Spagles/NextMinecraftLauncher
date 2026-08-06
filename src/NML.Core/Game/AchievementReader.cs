using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NML.Core.Game;

/// <summary>
/// Reads a Minecraft world's advancement (achievement) progress from its <c>advancements/*.json</c>
/// files. Each file is a JSON map of advancement-id → { "criteria": {...}, "done": bool }. The
/// reader summarizes how many advancements are done vs total, and lists the completed ones.
/// Pure + unit-tested.
/// </summary>
public static class AchievementReader
{
    /// <summary>Read all advancements for a world. Returns a summary + the completed IDs.</summary>
    public static AchievementSummary Read(string worldDir)
    {
        string advDir = Path.Combine(worldDir, "advancements");
        if (!Directory.Exists(advDir)) return new AchievementSummary();

        var allIds = new List<string>();
        var doneIds = new List<string>();

        foreach (string file in Directory.EnumerateFiles(advDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    // Skip recipe unlock entries (they're not real advancements).
                    if (prop.Name.StartsWith("minecraft:recipes/", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    allIds.Add(prop.Name);
                    if (prop.Value.TryGetProperty("done", out var done) && done.GetBoolean())
                        doneIds.Add(prop.Name);
                }
            }
            catch { /* skip unreadable files */ }
        }

        return new AchievementSummary(
            TotalAdvancements: allIds.Count,
            CompletedAdvancements: doneIds.Count,
            CompletedIds: doneIds.OrderBy(id => id).ToList());
    }
}

/// <summary>Summary of a world's advancement progress.</summary>
public sealed record AchievementSummary(
    int TotalAdvancements,
    int CompletedAdvancements,
    IReadOnlyList<string> CompletedIds)
{
    public AchievementSummary() : this(0, 0, System.Array.Empty<string>()) { }

    /// <summary>Percentage complete (0–100), or 0 when no advancements.</summary>
    public double PercentComplete => TotalAdvancements > 0
        ? System.Math.Round(CompletedAdvancements * 100.0 / TotalAdvancements, 1)
        : 0;

    /// <summary>Display string (e.g. "42 / 120 (35.0%)").</summary>
    public string Display => $"{CompletedAdvancements} / {TotalAdvancements} ({PercentComplete:F1}%)";
}
