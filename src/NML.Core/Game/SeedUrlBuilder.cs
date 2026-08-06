using System;
using System.Collections.Generic;

namespace NML.Core.Game;

/// <summary>
/// Builds URLs to online seed-preview services (Chunk Base biome/structure maps, SeedMap) from a
/// world seed, so the launcher can offer a one-click "view online" button. Pure + unit-tested.
/// <para>
/// The services use slightly different seed formats:
/// <list type="bullet">
/// <item><b>Chunk Base</b>: accepts a numeric seed in the URL query string.</item>
/// <item><b>SeedMap (seedmap.jarza.fr)</b>: same numeric seed in the path.</item>
/// </list>
/// </para>
/// </summary>
public static class SeedUrlBuilder
{
    /// <summary>Known seed-preview services the launcher can open.</summary>
    public enum Service
    {
        /// <summary>Chunk Base biome finder (chunkbase.com/apps/biome-finder).</summary>
        ChunkBase,
        /// <summary>Chunk Base structure finder (chunkbase.com/apps/structure-finder).</summary>
        ChunkBaseStructures,
        /// <summary>SeedMap (seedmap.jarza.fr).</summary>
        SeedMap,
    }

    /// <summary>Build the preview URL for a seed on the chosen service. Returns null for an
    /// unknown service or an empty seed.</summary>
    public static string? Build(Service service, long seed)
    {
        return service switch
        {
            Service.ChunkBase           => $"https://www.chunkbase.com/apps/biome-finder#seed/{seed}",
            Service.ChunkBaseStructures  => $"https://www.chunkbase.com/apps/structure-finder#seed/{seed}",
            Service.SeedMap              => $"https://seedmap.jarza.fr/#{seed}",
            _ => null,
        };
    }

    /// <summary>Build all available preview URLs for a seed.</summary>
    public static IReadOnlyDictionary<string, string> BuildAll(long seed)
    {
        return new Dictionary<string, string>
        {
            { "Chunk Base (Biomes)",     Build(Service.ChunkBase, seed)! },
            { "Chunk Base (Structures)", Build(Service.ChunkBaseStructures, seed)! },
            { "SeedMap",                 Build(Service.SeedMap, seed)! },
        };
    }
}
