using System;
using System.Collections.Generic;
using System.Linq;

namespace NML.Core.Modloaders;

/// <summary>
/// Checks whether a modloader + its version are compatible with a given Minecraft game version.
/// Uses known modloader version-range rules (Fabric/Quilt work on any version they support; Forge/
/// NeoForge are version-bound; OptiFine is version-bound). Pure + unit-tested.
/// <para>
/// The rules are derived from the modloaders' own version-naming conventions:
/// <list type="bullet">
/// <item><b>Fabric/Quilt</b>: the loader version string contains the game version it targets
///   (e.g. "fabric-loader-0.15.7-1.20.1") — or is universal (works on any version).</item>
/// <item><b>Forge</b>: the installer filename always includes the MC version
///   (e.g. "forge-1.20.1-47.2.0"). If the MC version in the string doesn't match, it's incompatible.</item>
/// <item><b>NeoForge</b>: version key is derived from the MC version (e.g. 1.20.1 → "20.1").</item>
/// <item><b>OptiFine</b>: filename includes the MC version (e.g. "OptiFine_1.20.1_HD_U_I6.jar").</item>
/// </list>
/// </para>
/// </summary>
public static class ModloaderCompatibilityChecker
{
    /// <summary>Known modloader identifiers.</summary>
    public const string Fabric = "fabric", Quilt = "quilt", Forge = "forge",
                         NeoForge = "neoforge", OptiFine = "optifine", Vanilla = "vanilla";

    /// <summary>Check compatibility. Returns a result with OK + reason.</summary>
    public static ModloaderCompatibility Check(string modloader, string loaderVersion, string gameVersion)
    {
        if (string.IsNullOrEmpty(modloader) || modloader.Equals(Vanilla, StringComparison.OrdinalIgnoreCase))
            return new ModloaderCompatibility(true, ModloaderCompatibilityReason.None, string.Empty);

        if (string.IsNullOrEmpty(gameVersion))
            return new ModloaderCompatibility(false, ModloaderCompatibilityReason.MissingGameVersion,
                "Game version is required to check modloader compatibility.");

        modloader = modloader.ToLowerInvariant().Trim();
        loaderVersion = loaderVersion ?? string.Empty;
        gameVersion = gameVersion.Trim();

        return modloader switch
        {
            Fabric or Quilt => CheckFabricQuilt(modloader, loaderVersion, gameVersion),
            Forge => CheckForge(loaderVersion, gameVersion),
            NeoForge => CheckNeoForge(loaderVersion, gameVersion),
            OptiFine => CheckOptiFine(loaderVersion, gameVersion),
            _ => new ModloaderCompatibility(true, ModloaderCompatibilityReason.None, string.Empty), // unknown → assume OK
        };
    }

    /// <summary>Fabric/Quilt: compatible when the loader version string either doesn't embed a game
    /// version (universal) or embeds one that matches.</summary>
    private static ModloaderCompatibility CheckFabricQuilt(string loader, string loaderVer, string gameVer)
    {
        // Universal loader versions (e.g. "0.15.7") don't contain a game version → assume compatible.
        // Version-bound loaders embed the game version (e.g. "0.15.7-1.20.1").
        if (!loaderVer.Contains(gameVer, StringComparison.OrdinalIgnoreCase))
        {
            // Check if the loader string embeds ANY game version (contains a pattern like x.y.z).
            // If it does and it's not the target → incompatible. If it doesn't → universal → OK.
            if (EmbedsGameVersion(loaderVer))
                return Incompatible($"{char.ToUpper(loader[0]) + loader[1..]} loader version '{loaderVer}' targets a different game version than '{gameVer}'.");
        }
        return Compatible();
    }

    /// <summary>Forge: the installer filename must contain the exact game version.</summary>
    private static ModloaderCompatibility CheckForge(string loaderVer, string gameVer)
    {
        if (!loaderVer.Contains(gameVer, StringComparison.OrdinalIgnoreCase))
            return Incompatible($"Forge version '{loaderVer}' does not match game version '{gameVer}'. Forge is version-bound — pick a Forge build for {gameVer}.");
        return Compatible();
    }

    /// <summary>NeoForge: the version key is derived from the MC version (1.20.1 → "20.1").</summary>
    private static ModloaderCompatibility CheckNeoForge(string loaderVer, string gameVer)
    {
        // NeoForge version string contains the derived key (e.g. "20.1" for 1.20.1).
        string neoKey = DeriveNeoForgeKey(gameVer);
        if (!string.IsNullOrEmpty(neoKey) && !loaderVer.Contains(neoKey, StringComparison.OrdinalIgnoreCase))
            return Incompatible($"NeoForge version '{loaderVer}' does not match game version '{gameVer}' (expected key '{neoKey}').");
        // Also check the full game version appears (newer NeoForge includes it).
        if (!loaderVer.Contains(gameVer, StringComparison.OrdinalIgnoreCase) && !loaderVer.Contains(neoKey, StringComparison.OrdinalIgnoreCase))
            return Incompatible($"NeoForge version '{loaderVer}' does not match game version '{gameVer}'.");
        return Compatible();
    }

    /// <summary>OptiFine: the filename must contain the exact game version.</summary>
    private static ModloaderCompatibility CheckOptiFine(string loaderVer, string gameVer)
    {
        if (!loaderVer.Contains(gameVer, StringComparison.OrdinalIgnoreCase))
            return Incompatible($"OptiFine version '{loaderVer}' does not match game version '{gameVer}'. OptiFine is version-bound.");
        return Compatible();
    }

    /// <summary>Derive the NeoForge short version key from an MC version (1.20.1 → "20.1").</summary>
    public static string DeriveNeoForgeKey(string gameVersion)
    {
        // Strip the leading "1." → "20.1".
        if (gameVersion.StartsWith("1.", StringComparison.Ordinal))
            return gameVersion[2..];
        return gameVersion;
    }

    /// <summary>Heuristic: does a loader version string embed a game version (x.y.z pattern)?</summary>
    private static bool EmbedsGameVersion(string version)
    {
        // Look for a pattern like "1.20.1" (digit.digit.digit) that isn't the loader's own semver.
        // A Fabric loader version like "0.15.7" has 3 parts but starts with 0.x → likely the loader version.
        // A game version starts with "1." → if the string contains "1.20" or similar, it embeds a game version.
        var parts = version.Split('-');
        foreach (var part in parts)
        {
            if (part.StartsWith("1.", StringComparison.Ordinal) && part.Contains('.'))
                return true;
        }
        return false;
    }

    private static ModloaderCompatibility Compatible() =>
        new(true, ModloaderCompatibilityReason.None, string.Empty);

    private static ModloaderCompatibility Incompatible(string message) =>
        new(false, ModloaderCompatibilityReason.VersionMismatch, message);
}

/// <summary>The outcome of a modloader compatibility check.</summary>
public sealed record ModloaderCompatibility(bool Ok, ModloaderCompatibilityReason Reason, string Message);

/// <summary>Why a modloader was flagged as incompatible (or None when OK).</summary>
public enum ModloaderCompatibilityReason
{
    None,
    VersionMismatch,
    MissingGameVersion,
}
