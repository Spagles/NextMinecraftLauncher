using NML.AICore;

namespace NML.AICore.Tests;

/// <summary>
/// Smoke test proving the AICore project resolves. Real AI-feature tests
/// (crash analysis, mod recommendation, natural-language config) arrive in M2.
/// </summary>
public class AiCoreSmokeTests
{
    [Fact]
    public void AiCore_assembly_is_loadable()
    {
        var type = typeof(AssemblyMarker);

        type.Should().NotBeNull();
        type.Namespace.Should().Be("NML.AICore");
    }
}
