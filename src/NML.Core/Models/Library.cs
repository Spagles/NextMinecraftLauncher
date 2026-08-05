using System.Text.Json.Serialization;
using NML.Core.Rules;

namespace NML.Core.Models;

/// <summary>
/// A single library entry in <c>libraries</c>. May be filtered by OS rules and may
/// carry native classifiers (LWJGL etc.) in addition to a single cross-platform artifact.
/// </summary>
public sealed class Library
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Maven coordinate parsed form: group:artifact:version. See <see cref="MavenCoordinate"/>.</summary>
    [JsonIgnore]
    public MavenCoordinate Coordinate => MavenCoordinate.Parse(Name);

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; init; }

    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; init; }

    /// <summary>Optional legacy "natives" map: OS name -> classifier suffix (e.g. "natives-windows").</summary>
    [JsonPropertyName("natives")]
    public IReadOnlyDictionary<string, string>? Natives { get; init; }

    /// <summary>URL base for the legacy <c>url</c>+<c>name</c>-path download form (old versions).</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>If true, extracted into the natives dir at launch (legacy LWJGL).</summary>
    [JsonPropertyName("extract")]
    public ExtractRule? Extract { get; init; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public Downloadable? Artifact { get; init; }

    /// <summary>Native JARs keyed by classifier (e.g. "natives-windows", "natives-linux").</summary>
    [JsonPropertyName("classifiers")]
    public IReadOnlyDictionary<string, Downloadable>? Classifiers { get; init; }
}

/// <summary>
/// A downloadable file with its SHA1, size and URL — the common shape used by client.jar,
/// server.jar, library artifacts and asset objects.
/// </summary>
public sealed class Downloadable
{
    [JsonPropertyName("sha1")]
    public string Sha1 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>Relative path under <c>.minecraft/libraries</c> (libraries only).</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed class ExtractRule
{
    /// <summary>Patterns to exclude when extracting the native JAR (e.g. META-INF/*).</summary>
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; init; }
}

public sealed class LoggingConfig
{
    [JsonPropertyName("file")]
    public LoggingFile? File { get; init; }

    [JsonPropertyName("argument")]
    public string? Argument { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class LoggingFile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string Sha1 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}
