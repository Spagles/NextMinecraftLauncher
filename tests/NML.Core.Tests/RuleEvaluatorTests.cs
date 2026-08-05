using NML.Core.Rules;

namespace NML.Core.Tests;

public class RuleEvaluatorTests
{
    private static RuleContext WindowsCtx() => new() { OsName = "windows", Arch = "x86_64" };
    private static RuleContext LinuxCtx() => new() { OsName = "linux", Arch = "x86_64" };
    private static RuleContext OsxCtx() => new() { OsName = "osx", Arch = "arm64" };

    [Fact]
    public void Null_or_empty_rules_means_always_allowed()
    {
        RuleEvaluator.IsAllowed(null, WindowsCtx()).Should().BeTrue();
        RuleEvaluator.IsAllowed(new List<Rule>(), LinuxCtx()).Should().BeTrue();
    }

    [Fact]
    public void Allow_rule_for_windows_only_allows_windows()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = "windows" } },
        };

        RuleEvaluator.IsAllowed(rules, WindowsCtx()).Should().BeTrue();
        RuleEvaluator.IsAllowed(rules, LinuxCtx()).Should().BeFalse();
        RuleEvaluator.IsAllowed(rules, OsxCtx()).Should().BeFalse();
    }

    [Fact]
    public void Default_allow_then_disallow_osx()
    {
        // The common LWJGL pattern: allowed everywhere except osx via a "disallow osx" rule.
        // Represented as two rules: implicit allow (no os) + disallow osx.
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new OsRule { Name = "osx" } },
        };

        RuleEvaluator.IsAllowed(rules, WindowsCtx()).Should().BeTrue();
        RuleEvaluator.IsAllowed(rules, LinuxCtx()).Should().BeTrue();
        RuleEvaluator.IsAllowed(rules, OsxCtx()).Should().BeFalse();
    }

    [Fact]
    public void Last_matching_rule_wins()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = "windows" } },
            new() { Action = "disallow", Os = new OsRule { Name = "windows", Arch = "x86_64" } },
        };

        // windows/x86_64 matches both; the disallow is last → disallowed.
        RuleEvaluator.IsAllowed(rules, WindowsCtx()).Should().BeFalse();
    }

    [Fact]
    public void Feature_gated_rule_requires_the_feature()
    {
        var ctx = new RuleContext { OsName = "windows", Features = new Dictionary<string, bool>() };
        var rules = new List<Rule>
        {
            new() { Action = "allow", Features = new Dictionary<string, bool> { ["is_demo_user"] = true } },
        };

        RuleEvaluator.IsAllowed(rules, ctx).Should().BeFalse();

        var ctx2 = new RuleContext
        {
            OsName = "windows",
            Features = new Dictionary<string, bool> { ["is_demo_user"] = true },
        };
        RuleEvaluator.IsAllowed(rules, ctx2).Should().BeTrue();
    }
}
