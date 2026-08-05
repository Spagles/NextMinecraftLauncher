using System.Globalization;

namespace NML.Core.Models;

/// <summary>
/// A parsed Maven coordinate <c>group:artifact:version</c> (optionally with a
/// classifier). Provides the library layout that <c>.minecraft/libraries</c> mirrors.
/// </summary>
public readonly record struct MavenCoordinate(
    string Group, string Artifact, string Version, string? Classifier)
{
    public static MavenCoordinate Parse(string name)
    {
        // group:artifact:version[:classifier]
        string[] parts = name.Split(':');
        return parts.Length switch
        {
            >= 4 => new(parts[0], parts[1], parts[2], parts[3]),
            3    => new(parts[0], parts[1], parts[2], null),
            _    => throw new FormatException($"Invalid Maven coordinate: '{name}'."),
        };
    }

    /// <summary>The relative path under <c>.minecraft/libraries</c>, matching Maven layout.</summary>
    public string RelativePath
    {
        get
        {
            string groupPath = Group.Replace('.', '/');
            string file = Classifier is null
                ? $"{Artifact}-{Version}.jar"
                : $"{Artifact}-{Version}-{Classifier}.jar";
            return $"{groupPath}/{Artifact}/{Version}/{file}";
        }
    }

    public override string ToString() =>
        Classifier is null ? $"{Group}:{Artifact}:{Version}" : $"{Group}:{Artifact}:{Version}:{Classifier}";
}
