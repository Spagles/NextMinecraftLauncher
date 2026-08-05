using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.Core.Modloaders.Forge;

/// <summary>
/// The <c>install_profile.json</c> document for a Forge installer (legacy 1.12- and modern
/// 1.13+ shapes). The modern shape's <c>processors</c> array is what gets executed to
/// transform the vanilla jar into a runnable Forge jar (deobfuscation via SpecialSource,
/// binary patching, jar signing). This model is the contract the launcher parses against.
/// </summary>
public sealed class ForgeInstallProfile
{
    /// <summary>The Forge version this installer targets (e.g. "37.0.42").</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>The vanilla game version it builds on (e.g. "1.18.1").</summary>
    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; init; }

    /// <summary>The full versionInfo to write as the profile's version.json.</summary>
    [JsonPropertyName("versionInfo")]
    public JsonElement? VersionInfo { get; init; }

    /// <summary>The processors to run, in order, after libraries are downloaded.</summary>
    [JsonPropertyName("processors")]
    public List<ForgeProcessor>? Processors { get; init; }

    /// <summary>Library coordinates required to run the processors (executor classpath).</summary>
    [JsonPropertyName("libraries")]
    public List<ForgeLibrary>? Libraries { get; init; }

    /// <summary>Legacy (1.12-) install path data — the older shape embeds install data differently.</summary>
    [JsonPropertyName("install")]
    public ForgeLegacyInstall? LegacyInstall { get; init; }
}

/// <summary>
/// A single processor step. Each processor is a small Java tool (its jar named by
/// <see cref="Jar"/>) run with a classpath (<see cref="Classpath"/>) and a list of
/// <see cref="Args"/>. <see cref="Sides"/> restricts which install sides (client/server)
/// the processor runs on.
/// </summary>
public sealed class ForgeProcessor
{
    /// <summary>Maven coordinate of the JAR containing the processor's main class.</summary>
    [JsonPropertyName("jar")]
    public string? Jar { get; init; }

    /// <summary>Maven coordinates of additional libraries on the processor classpath.</summary>
    [JsonPropertyName("classpath")]
    public List<string>? Classpath { get; init; }

    /// <summary>Argument tokens; literal strings or variables (enclosed in square brackets).</summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; init; }

    /// <summary>Output file outputs used by later processors (token replacement targets).</summary>
    [JsonPropertyName("outputs")]
    public Dictionary<string, string>? Outputs { get; init; }

    /// <summary>Restrict to "client" or "server" install sides; null/empty = both.</summary>
    [JsonPropertyName("sides")]
    public List<string>? Sides { get; init; }
}

/// <summary>A library entry shared between the install_profile and version shapes.</summary>
public sealed class ForgeLibrary
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Legacy only: a base URL appended before the maven path.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Checksum hint (legacy only).</summary>
    [JsonPropertyName("checksums")]
    public List<string>? Checksums { get; init; }

    [JsonPropertyName("client")]
    public ForgeArtifactRef? Client { get; init; }

    [JsonPropertyName("server")]
    public ForgeArtifactRef? Server { get; init; }
}

public sealed class ForgeArtifactRef
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }
}

public sealed class ForgeLegacyInstall
{
    /// <summary>Path/name tokens used by the legacy installer.</summary>
    [JsonPropertyName("profileName")]
    public string? ProfileName { get; init; }

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("minecraft")]
    public string? Minecraft { get; init; }
}
