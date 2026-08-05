using NML.Core.Rules;

namespace NML.Core.Rules;

/// <summary>
/// Evaluates a list of <see cref="Rule"/>s against a <see cref="RuleContext"/>.
/// Mojang's semantics: rules are evaluated in order; the last matching rule's
/// action wins. If no rules are present, the element is allowed unconditionally.
/// </summary>
public static class RuleEvaluator
{
    /// <summary>
    /// Returns true if the element carrying <paramref name="rules"/> should be
    /// included for <paramref name="ctx"/>. Semantics (matching the official launcher):
    /// <list type="bullet">
    /// <item>No rules at all → always allowed (default-include).</item>
    /// <item>Rules present but none match the context → disallowed (excluded).</item>
    /// <item>Otherwise the last matching rule's action wins (allow/disallow).</item>
    /// </list>
    /// </summary>
    public static bool IsAllowed(IReadOnlyList<Rule>? rules, RuleContext ctx)
    {
        if (rules is null || rules.Count == 0)
            return true;

        bool matchedAny = false;
        bool allowed = false;
        foreach (Rule rule in rules)
        {
            if (!rule.Matches(ctx)) continue;
            matchedAny = true;
            allowed = rule.IsAllow;
        }
        // If no rule matched, the element is excluded (this is what makes a lone
        // "allow windows" rule correctly exclude Linux/macOS).
        return matchedAny && allowed;
    }
}
