using System.Text.Json.Serialization;

namespace NML.Core.Models;

/// <summary>
/// The parsed asset index document — a map of asset path → {hash, size}.
/// Assets are stored under <c>.minecraft/assets/objects/&lt;hash[0..2]&gt;/&lt;hash&gt;</c>.
/// </summary>
public sealed class AssetIndex
{
    [JsonPropertyName("objects")]
    public IReadOnlyDictionary<string, AssetObject> Objects { get; init; }
        = new Dictionary<string, AssetObject>();

    [JsonPropertyName("virtual")]
    public bool Virtual { get; init; }

    [JsonPropertyName("map_to_resources")]
    public bool MapToResources { get; init; }
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}
