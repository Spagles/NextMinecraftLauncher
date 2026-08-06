using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NML.Core.Game;

/// <summary>
/// Generates common Minecraft commands (/give, /tp, /effect, /gamemode, /time, /weather) from
/// structured parameters so the launcher's command-block UI can build commands without the user
/// memorizing syntax. Pure + unit-tested.
/// </summary>
public static class MinecraftCommandBuilder
{
    // --- /give ---

    /// <summary>Build a /give command. "minecraft:diamond_sword" is auto-prefixed when no namespace.</summary>
    public static string Give(string target, string itemId, int count = 1, int? enchantLevel = null, string? enchantId = null)
    {
        target = SanitizeTarget(target);
        itemId = EnsureNamespace(itemId);
        var sb = new StringBuilder($"/give {target} {itemId}");
        if (count != 1) sb.Append(' ').Append(count);

        // Enchantments via NBT when an enchant id + level are specified.
        if (!string.IsNullOrWhiteSpace(enchantId) && enchantLevel.HasValue)
        {
            string ench = EnsureNamespace(enchantId!);
            sb.Append($"{{Enchantments:[{{id:\"{ench}\",lvl:{enchantLevel.Value}}}]}}");
        }
        return sb.ToString();
    }

    // --- /tp (teleport) ---

    /// <summary>Build a /tp command to coordinates.</summary>
    public static string Teleport(string target, double x, double y, double z)
        => $"/tp {SanitizeTarget(target)} {x:F1} {y:F1} {z:F1}";

    /// <summary>Build a /tp command to another player/entity.</summary>
    public static string TeleportTo(string target, string destination)
        => $"/tp {SanitizeTarget(target)} {SanitizeTarget(destination)}";

    // --- /effect ---

    /// <summary>Build a /effect give command.</summary>
    public static string EffectGive(string target, string effectId, int durationSeconds = 30, int amplifier = 0, bool particles = true)
    {
        target = SanitizeTarget(target);
        effectId = EnsureNamespace(effectId);
        return $"/effect give {target} {effectId} {durationSeconds} {amplifier} {(particles ? "true" : "false")}";
    }

    // --- /gamemode ---

    /// <summary>Build a /gamemode command. mode: "survival" / "creative" / "adventure" / "spectator".</summary>
    public static string Gamemode(string target, string mode)
    {
        target = SanitizeTarget(target);
        mode = mode.ToLowerInvariant().Trim();
        var valid = new HashSet<string> { "survival", "creative", "adventure", "spectator", "0", "1", "2", "3" };
        if (!valid.Contains(mode)) mode = "survival"; // safe default
        return $"/gamemode {mode} {target}";
    }

    // --- /time ---

    /// <summary>Build a /time set command. action: "day" / "night" / "noon" / "midnight" or a number.</summary>
    public static string TimeSet(string action)
        => $"/time set {action.ToLowerInvariant().Trim()}";

    // --- /weather ---

    /// <summary>Build a /weather command. type: "clear" / "rain" / "thunder".</summary>
    public static string Weather(string type, int? duration = null)
    {
        type = type.ToLowerInvariant().Trim();
        var valid = new HashSet<string> { "clear", "rain", "thunder" };
        if (!valid.Contains(type)) type = "clear";
        return duration.HasValue ? $"/weather {type} {duration.Value}" : $"/weather {type}";
    }

    // --- helpers ---

    /// <summary>Ensure an item/effect id has a namespace prefix (default "minecraft:").</summary>
    public static string EnsureNamespace(string id)
    {
        id = id.Trim();
        return id.Contains(':') ? id : $"minecraft:{id}";
    }

    /// <summary>Sanitize a target selector or player name (strip spaces, allow @p/@a/@s/@e/@r).</summary>
    public static string SanitizeTarget(string target)
    {
        target = target.Trim();
        // Selectors are safe as-is.
        if (target.StartsWith('@')) return target;
        // Player names: no spaces allowed.
        return target.Replace(" ", "_");
    }
}
