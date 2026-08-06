using NML.Core.Game;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="SeedUrlBuilder"/> — builds URLs to online seed-preview services.
/// </summary>
public class SeedUrlBuilderTests
{
    [Fact]
    public void Build_ChunkBase_Contains_Seed()
    {
        string url = SeedUrlBuilder.Build(SeedUrlBuilder.Service.ChunkBase, 12345L)!;
        url.Should().StartWith("https://www.chunkbase.com/apps/biome-finder#seed/");
        url.Should().Contain("12345");
    }

    [Fact]
    public void Build_ChunkBaseStructures_Contains_Seed()
    {
        string url = SeedUrlBuilder.Build(SeedUrlBuilder.Service.ChunkBaseStructures, -999L)!;
        url.Should().StartWith("https://www.chunkbase.com/apps/structure-finder#seed/");
        url.Should().Contain("-999");
    }

    [Fact]
    public void Build_SeedMap_Contains_Seed()
    {
        string url = SeedUrlBuilder.Build(SeedUrlBuilder.Service.SeedMap, 42L)!;
        url.Should().StartWith("https://seedmap.jarza.fr/#");
        url.Should().Contain("42");
    }

    [Fact]
    public void Build_Unknown_Service_Returns_Null()
    {
        SeedUrlBuilder.Build((SeedUrlBuilder.Service)999, 1L).Should().BeNull();
    }

    [Fact]
    public void BuildAll_Returns_All_Services()
    {
        var urls = SeedUrlBuilder.BuildAll(100L);
        urls.Should().HaveCount(3);
        urls.Should().ContainKey("Chunk Base (Biomes)");
        urls.Should().ContainKey("Chunk Base (Structures)");
        urls.Should().ContainKey("SeedMap");
        urls.Values.Should().OnlyContain(u => u.Contains("100"));
    }
}
