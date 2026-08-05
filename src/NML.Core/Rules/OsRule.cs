using System.Text.Json.Serialization;

namespace NML.Core.Rules;

/// <summary>
/// An OS/arch matching predicate that appears in version.json library and argument
/// rules. Any omitted field matches everything (wildcard). Mojang's semantics:
/// the rule "allows" when its predicate matches the current <see cref="RuleContext"/>.
/// </summary>
public sealed class OsRule
{
    /// <summary>Mojang OS name: <c>windows</c>, <c>linux</c>, <c>osx</c>. Null = any.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>e.g. <c>x86</c>, <c>x86_64</c>, <c>arm64</c>. Null = any.</summary>
    [JsonPropertyName("arch")]
    public string? Arch { get; init; }

    /// <summary>Regex matched against the OS version string (Windows-only in practice).</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>True to disallow on matching platforms (inverse predicate).</summary>
    [JsonPropertyName("versionRange")]
    public VersionRange? VersionRange { get; init; }

    public bool Matches(RuleContext ctx)
    {
        if (Name is not null && !string.Equals(Name, ctx.OsName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Arch is not null && !string.Equals(Arch, ctx.Arch, StringComparison.OrdinalIgnoreCase))
            return false;

        if (VersionRange is not null && !VersionRange.Matches(ctx.OsVersion))
            return false;

        // "version" regex (legacy) — applied against the OS version string when present.
        if (Version is not null)
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        ctx.OsVersion ?? string.Empty, Version, default,
                        TimeSpan.FromSeconds(2)))
                    return false;
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern — treat as no match rather than crashing the launch.
                return false;
            }
        }

        return true;
    }
}

public sealed class VersionRange
{
    [JsonPropertyName("min")]
    public string? Min { get; init; }

    [JsonPropertyName("max")]
    public string? Max { get; init; }

    public bool Matches(string? version)
    {
        if (string.IsNullOrEmpty(version)) return false;

        if (Min is not null && string.CompareOrdinal(version, Min) < 0) return false;
        if (Max is not null && string.CompareOrdinal(version, Max) > 0) return false;
        return true;
    }
}
