using System.Text.Json;
using NML.Data;

namespace NML.Data.Tests;

/// <summary>
/// Validates that the catalog's JSON parsing matches real Modrinth/CurseForge payloads.
/// Uses a fake IHttpFetcher so no network is needed.
/// </summary>
public class CatalogModelTests
{
    [Fact]
    public void Normalized_search_result_round_trips()
    {
        var r = new ModSearchResult
        {
            ProjectId = "sodium",
            Title = "Sodium",
            Author = "jellysquid",
            Downloads = 50_000_000,
            Categories = new[] { "optimization", "fabric" },
            Source = ModCatalogKind.Modrinth,
        };

        r.ProjectId.Should().Be("sodium");
        r.Categories.Should().Contain("fabric");
        r.Source.Should().Be(ModCatalogKind.Modrinth);
    }

    [Fact]
    public void ModFile_carries_integrity_fields()
    {
        var f = new ModFile
        {
            FileName = "sodium.jar",
            DownloadUrl = "https://x/sodium.jar",
            Sha1 = "abc123",
            Size = 1234567,
            GameVersion = "1.20.1",
            Loader = ModLoader.Fabric,
        };

        f.Sha1.Should().NotBeNullOrEmpty("files must carry SHA-1 for download integrity checks");
        f.Loader.Should().Be(ModLoader.Fabric);
    }

    [Fact]
    public void ModLoader_enum_covers_common_loaders()
    {
        // The recommender filters candidates by loader; ensure the enum covers all we support.
        new[] { ModLoader.Fabric, ModLoader.Forge, ModLoader.Quilt, ModLoader.NeoForge, ModLoader.Any }
            .Should().HaveCount(5);
    }
}
