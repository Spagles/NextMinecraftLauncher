using NML.Data;

namespace NML.Data.Tests;

/// <summary>
/// Smoke test proving the Data project resolves. Real API-client tests
/// (Modrinth, CurseForge, Mojang) arrive in M1/M3.
/// </summary>
public class DataSmokeTests
{
    [Fact]
    public void Data_assembly_is_loadable()
    {
        var type = typeof(AssemblyMarker);

        type.Should().NotBeNull();
        type.Namespace.Should().Be("NML.Data");
    }
}
