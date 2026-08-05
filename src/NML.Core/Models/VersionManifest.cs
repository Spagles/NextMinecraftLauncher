using System.Text.Json.Serialization;

namespace NML.Core.Models;

/// <summary>
/// The top-level <c>version_manifest_v2.json</c> document from piston-meta.mojang.com.
/// </summary>
public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public VersionLatest Latest { get; init; } = new();

    [JsonPropertyName("versions")]
    public List<VersionManifestEntry> Versions { get; init; } = new();
}

public sealed class VersionLatest
{
    [JsonPropertyName("release")]
    public string Release { get; init; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; init; } = string.Empty;
}

public sealed class VersionManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; init; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; init; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; init; }
}
