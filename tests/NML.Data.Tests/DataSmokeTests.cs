namespace NML.Data.Tests;

/// <summary>
/// Smoke test proving the Data test harness runs and the catalog interface resolves.
/// Real catalog tests live alongside.
/// </summary>
public class DataSmokeTests
{
    [Fact]
    public void Catalog_interface_and_kinds_are_accessible()
    {
        ModCatalogKind.Modrinth.Should().Be(ModCatalogKind.Modrinth);
        ModCatalogKind.CurseForge.Should().NotBe(ModCatalogKind.Modrinth);
    }
}
