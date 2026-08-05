using System.Text.Json.Serialization;

namespace NML.Core.Rules;

/// <summary>
/// A single rule entry from version.json (the items inside <c>rules</c> arrays).
/// A rule either <see cref="Allow"/>s or disallows the attached element based on
/// whether its <see cref="Os"/>/<see cref="Features"/> predicate matches.
/// </summary>
public sealed class Rule
{
    /// <summary><c>allow</c> or <c>disallow</c>.</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "allow";

    /// <summary>Optional OS predicate. Missing = matches every OS.</summary>
    [JsonPropertyName("os")]
    public OsRule? Os { get; init; }

    /// <summary>Optional feature gate (e.g. <c>is_demo_user</c>, <c>has_custom_resolution</c>).</summary>
    [JsonPropertyName("features")]
    public IReadOnlyDictionary<string, bool>? Features { get; init; }

    public bool IsAllow => string.Equals(Action, "allow", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Does this rule's predicate match the given context (and optional feature set)?
    /// </summary>
    public bool Matches(RuleContext ctx)
    {
        // OS predicate: if present, must match.
        if (Os is not null && !Os.Matches(ctx))
            return false;

        // Feature predicate: every required feature must be set in the launch context.
        if (Features is not null && Features.Count > 0)
        {
            foreach ((string key, bool required) in Features)
            {
                if (!ctx.Features.TryGetValue(key, out bool actual) || actual != required)
                    return false;
            }
        }

        return true;
    }
}
