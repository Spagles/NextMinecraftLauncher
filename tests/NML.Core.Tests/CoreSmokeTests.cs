using NML.Core;

namespace NML.Core.Tests;

/// <summary>
/// Smoke test proving the test harness runs and Core resolves. Real domain
/// tests (download, auth, modloaders) arrive in M1.
/// </summary>
public class CoreSmokeTests
{
    [Fact]
    public void Core_assembly_is_loadable()
    {
        // The mere act of referencing the marker type exercises the project
        // reference; if it failed to resolve, the test would not compile/run.
        var type = typeof(AssemblyMarker);

        type.Should().NotBeNull();
        type.Namespace.Should().Be("NML.Core");
    }
}
