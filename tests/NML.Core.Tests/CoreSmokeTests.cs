using NML.Core.Models;

namespace NML.Core.Tests;

/// <summary>
/// Smoke test proving the test harness runs and Core resolves. The real domain
/// tests live alongside; this guards the project-reference wiring.
/// </summary>
public class CoreSmokeTests
{
    [Fact]
    public void JsonOptions_and_argument_factories_are_accessible()
    {
        // Touch a representative type from each Core sub-namespace to prove references work.
        JsonOptions.Default.Should().NotBeNull();
        ArgumentElement.FromLiteral("--x").IsConditional.Should().BeFalse();
    }
}
