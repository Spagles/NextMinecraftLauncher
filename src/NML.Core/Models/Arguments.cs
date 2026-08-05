using System.Text.Json.Serialization;
using NML.Core.Rules;

namespace NML.Core.Models;

/// <summary>
/// Modern (1.13+) arguments container. Each of <see cref="Game"/>/<see cref="Jvm"/>
/// is a mixed list of literal strings and <see cref="ConditionalArgument"/> entries
/// gated by rules (e.g. OS-specific <c>-XstartOnFirstThread</c> on macOS).
/// </summary>
public sealed class Arguments
{
    [JsonPropertyName("game")]
    public List<ArgumentElement> Game { get; init; } = new();

    [JsonPropertyName("jvm")]
    public List<ArgumentElement> Jvm { get; init; } = new();
}

/// <summary>
/// A polymorphic argument element: either a plain string (always included) or an
/// object with <c>{ value, rules }</c> (conditionally included).
/// </summary>
public sealed class ArgumentElement
{
    // Set when the element is a plain string.
    [JsonIgnore]
    public string? Literal { get; private init; }

    // Set when the element is a conditional object.
    [JsonIgnore]
    public IReadOnlyList<string>? Values { get; private init; }

    [JsonIgnore]
    public IReadOnlyList<Rule>? Rules { get; private init; }

    [JsonIgnore]
    public bool IsConditional => Rules is not null;

    /// <summary>Create from a literal string element.</summary>
    public static ArgumentElement FromLiteral(string s) => new() { Literal = s };

    /// <summary>Create from a conditional {value, rules} element.</summary>
    public static ArgumentElement FromConditional(IReadOnlyList<string> values, IReadOnlyList<Rule> rules) =>
        new() { Values = values, Rules = rules };
}
