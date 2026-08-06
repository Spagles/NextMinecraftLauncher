using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NML.Core.Game;

/// <summary>
/// Reads a Minecraft world's play statistics from its <c>stats/*.json</c> files. Modern Minecraft
/// (1.13+) stores stats as a flat JSON object under <c>{"stats": {"minecraft:custom": {"minecraft:play_time": 12345, ...}}}</c>.
/// The reader extracts the most commonly-requested stats into a summary so the launcher can display
/// playtime, distance, kills, etc. without launching the game. Pure + unit-tested.
/// </summary>
public static class WorldStatsReader
{
    /// <summary>The stat keys the summary surfaces (mapped to friendly labels).</summary>
    public static readonly IReadOnlyDictionary<string, string> TrackedStats = new Dictionary<string, string>
    {
        { "minecraft:play_time",          "Play Time (ticks)" },
        { "minecraft:walk_one_cm",        "Distance Walked (cm)" },
        { "minecraft:sprint_one_cm",      "Distance Sprinted (cm)" },
        { "minecraft:jump",               "Jumps" },
        { "minecraft:mob_kills",          "Mob Kills" },
        { "minecraft:player_kills",       "Player Kills" },
        { "minecraft:deaths",             "Deaths" },
        { "minecraft:damage_dealt",       "Damage Dealt" },
        { "minecraft:damage_taken",       "Damage Taken" },
        { "minecraft:blocks_mined",      "Blocks Mined" }, // Note: this is a type-specific stat in 1.13+, but we surface the total if present.
        { "minecraft:fish_caught",        "Fish Caught" },
        { "minecraft:animals_bred",       "Animals Bred" },
        { "minecraft:leave_game",         "Sessions" },
    };

    /// <summary>Read stats from a world dir. Returns an empty summary when no stats exist.</summary>
    public static WorldStatsSummary Read(string worldDir)
    {
        string statsDir = Path.Combine(worldDir, "stats");
        if (!Directory.Exists(statsDir)) return new WorldStatsSummary();

        var entries = new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(statsDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);

                // Modern format: { "stats": { "minecraft:custom": { "minecraft:play_time": 12345, ... } } }
                if (doc.RootElement.TryGetProperty("stats", out var stats))
                {
                    // Iterate all sub-categories (minecraft:custom, minecraft:mined, minecraft:killed, etc.)
                    foreach (var category in stats.EnumerateObject())
                    {
                        if (category.Value.ValueKind != JsonValueKind.Object) continue;
                        foreach (var stat in category.Value.EnumerateObject())
                        {
                            // Sum across categories for the same stat key.
                            long value = stat.Value.ValueKind == JsonValueKind.Number ? stat.Value.GetInt64() : 0;
                            if (entries.TryGetValue(stat.Name, out long existing))
                                entries[stat.Name] = existing + value;
                            else
                                entries[stat.Name] = value;
                        }
                    }
                }
            }
            catch { /* skip unreadable files */ }
        }

        // Build the summary from tracked stats only.
        var tracked = new Dictionary<string, WorldStatEntry>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (key, label) in TrackedStats)
        {
            if (entries.TryGetValue(key, out long value) && value > 0)
                tracked[key] = new WorldStatEntry(label, value);
        }

        // Compute blocks mined total from minecraft:mined:* entries.
        long totalMined = entries
            .Where(kvp => kvp.Key.StartsWith("minecraft:mined/", System.StringComparison.OrdinalIgnoreCase))
            .Sum(kvp => kvp.Value);
        if (totalMined > 0 && !tracked.ContainsKey("minecraft:blocks_mined"))
            tracked["minecraft:blocks_mined"] = new WorldStatEntry("Blocks Mined", totalMined);

        // Play time in minutes (ticks / 20 / 60).
        long playTimeTicks = entries.GetValueOrDefault("minecraft:play_time", 0);
        int playTimeMinutes = (int)(playTimeTicks / (20 * 60));

        return new WorldStatsSummary(tracked, playTimeMinutes, entries.Count);
    }
}

/// <summary>A single tracked stat with a friendly label + raw value.</summary>
public sealed record WorldStatEntry(string Label, long Value);

/// <summary>Summary of a world's play statistics.</summary>
public sealed record WorldStatsSummary(
    IReadOnlyDictionary<string, WorldStatEntry> TrackedStats,
    int PlayTimeMinutes,
    int TotalRawStatCount)
{
    public WorldStatsSummary() : this(new Dictionary<string, WorldStatEntry>(), 0, 0) { }

    /// <summary>Display string for play time (e.g. "2h 15m").</summary>
    public string PlayTimeDisplay => PlayTimeMinutes switch
    {
        0 => "0m",
        < 60 => $"{PlayTimeMinutes}m",
        _ => $"{PlayTimeMinutes / 60}h {PlayTimeMinutes % 60}m",
    };
}
