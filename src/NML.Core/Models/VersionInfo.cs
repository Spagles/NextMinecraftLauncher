using System.Text.Json.Serialization;
using NML.Core.Rules;

namespace NML.Core.Models;

/// <summary>
/// A fully-parsed individual version.json document (downloaded from a manifest
/// entry's <c>url</c>). Covers both the modern <c>arguments</c> form and the
/// legacy <c>minecraftArguments</c> form.
/// </summary>
public sealed class VersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; init; }

    [JsonPropertyName("assets")]
    public string? Assets { get; init; }

    [JsonPropertyName("assetIndex")]
    public AssetIndexRef? AssetIndex { get; init; }

    [JsonPropertyName("downloads")]
    public VersionDownloads? Downloads { get; init; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; init; } = new();

    /// <summary>Modern (1.13+) argument format.</summary>
    [JsonPropertyName("arguments")]
    public Arguments? Arguments { get; init; }

    /// <summary>Legacy (pre-1.13) argument format, space-separated.</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; init; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionRef? JavaVersion { get; init; }

    [JsonPropertyName("logging")]
    public IReadOnlyDictionary<string, LoggingConfig>? Logging { get; init; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; init; }

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; init; }

    /// <summary>If set, this version inherits (and is merged with) the named parent version.</summary>
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; init; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; init; }
}

public sealed class AssetIndexRef
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string Sha1 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")]
    public Downloadable? Client { get; init; }

    [JsonPropertyName("client_mappings")]
    public Downloadable? ClientMappings { get; init; }

    [JsonPropertyName("server")]
    public Downloadable? Server { get; init; }

    [JsonPropertyName("server_mappings")]
    public Downloadable? ServerMappings { get; init; }

    [JsonPropertyName("windows_server")]
    public Downloadable? WindowsServer { get; init; }
}

public sealed class JavaVersionRef
{
    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; init; }
}
