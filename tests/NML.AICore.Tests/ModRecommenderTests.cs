using NML.AICore.Features;
using NML.AICore.Providers;
using NML.Data;

namespace NML.AICore.Tests;

/// <summary>
/// The core anti-hallucination guarantee: the recommender may only return mods that came
/// from the catalog (real ids). Any id the LLM invents is dropped. These tests pin that.
/// </summary>
public class ModRecommenderTests
{
    private static readonly IReadOnlyList<ModSearchResult> Candidates = new[]
    {
        new ModSearchResult { ProjectId = "sodium", Title = "Sodium", Author = "jellysquid", Downloads = 50000000, Categories = new[] { "optimization" } },
        new ModSearchResult { ProjectId = "lithium", Title = "Lithium", Author = "jellysquid", Downloads = 20000000, Categories = new[] { "optimization" } },
        new ModSearchResult { ProjectId = "iris", Title = "Iris Shaders", Author = "coderbot", Downloads = 15000000, Categories = new[] { "shaders" } },
    };

    [Fact]
    public void Maps_real_ids_back_to_candidates()
    {
        string modelReply = """
            {"picks":[{"id":"sodium","reason":"best fps boost"},{"id":"lithium","reason":"server-side perf"}]}
            """;

        IReadOnlyList<ModRecommendation> picks = ModRecommender.Map(modelReply, Candidates, maxResults: 5);

        picks.Should().HaveCount(2);
        picks[0].Mod.ProjectId.Should().Be("sodium");
        picks[0].Reason.Should().Contain("fps boost");
        picks[0].Rank.Should().Be(1);
        picks[1].Mod.ProjectId.Should().Be("lithium");
        picks[1].Rank.Should().Be(2);
    }

    [Fact]
    public void Drops_hallucinated_ids_not_in_candidates()
    {
        // The model "recommends" two real ids plus one invented one ("totally-real-mod").
        string modelReply = """
            {"picks":[
              {"id":"sodium","reason":"ok"},
              {"id":"totally-real-mod","reason":"trust me"},
              {"id":"iris","reason":"shaders"}
            ]}
            """;

        IReadOnlyList<ModRecommendation> picks = ModRecommender.Map(modelReply, Candidates, maxResults: 5);

        // The hallucinated id must be silently dropped — only real candidates survive.
        picks.Should().HaveCount(2);
        picks.Select(p => p.Mod.ProjectId).Should().BeEquivalentTo(new[] { "sodium", "iris" });
        picks.Should().NotContain(p => p.Mod.ProjectId == "totally-real-mod");
    }

    [Fact]
    public void Respects_max_results()
    {
        string modelReply = """
            {"picks":[{"id":"sodium","reason":""},{"id":"lithium","reason":""},{"id":"iris","reason":""}]}
            """;

        IReadOnlyList<ModRecommendation> picks = ModRecommender.Map(modelReply, Candidates, maxResults: 2);

        picks.Should().HaveCount(2);
    }

    [Fact]
    public void Returns_empty_when_model_picks_nothing()
    {
        string modelReply = """{"picks":[]}""";
        IReadOnlyList<ModRecommendation> picks = ModRecommender.Map(modelReply, Candidates, maxResults: 5);
        picks.Should().BeEmpty();
    }

    [Fact]
    public void Returns_empty_on_garbage()
    {
        IReadOnlyList<ModRecommendation> picks = ModRecommender.Map("not json", Candidates, maxResults: 5);
        picks.Should().BeEmpty();
    }

    [Fact]
    public async Task End_to_end_uses_catalog_then_constrains_to_reals()
    {
        // The fake catalog returns our 3 candidates; the fake client "picks" sodium + a
        // hallucination. The hallucination must be filtered out.
        var catalog = new FakeCatalog(Candidates);
        var fake = new FakeChatClient("""{"picks":[{"id":"sodium","reason":"fast"},{"id":"invented","reason":"x"}]}""");
        var rec = new ModRecommender(fake, Microsoft.Extensions.Logging.Abstractions.NullLogger<ModRecommender>.Instance);

        IReadOnlyList<ModRecommendation> result = await rec.RecommendAsync(
            catalog, "make my game faster", gameVersion: "1.20.1", loader: ModLoader.Fabric);

        catalog.SearchCallCount.Should().Be(1);
        result.Should().ContainSingle();
        result[0].Mod.ProjectId.Should().Be("sodium");
    }

    [Fact]
    public void DeriveSearchQuery_strips_stop_words()
    {
        string q = ModRecommender.DeriveSearchQuery("I want a mod for better graphics");
        q.Should().Be("better graphics");
    }

    [Fact]
    public void BuildPrompt_lists_candidates_with_their_ids()
    {
        string prompt = ModRecommender.BuildPrompt("fps", Candidates);
        prompt.Should().Contain("id=sodium");
        prompt.Should().Contain("Sodium");
        prompt.Should().Contain("50000000 downloads");
    }
}

/// <summary>A fake catalog that returns a fixed candidate list (for recommender tests).</summary>
internal sealed class FakeCatalog : IModCatalog
{
    private readonly IReadOnlyList<ModSearchResult> _candidates;
    public int SearchCallCount { get; private set; }
    public FakeCatalog(IReadOnlyList<ModSearchResult> candidates) => _candidates = candidates;
    public ModCatalogKind Kind => ModCatalogKind.Modrinth;
    public Task<IReadOnlyList<ModSearchResult>> SearchAsync(string query, string? gameVersion = null, ModLoader? loader = null, int limit = 20, CancellationToken ct = default)
    { SearchCallCount++; return Task.FromResult(_candidates); }
    public Task<ModProject?> GetProjectAsync(string projectId, CancellationToken ct = default) =>
        Task.FromResult<ModProject?>(null);
    public Task<IReadOnlyList<ModFile>> GetFilesAsync(string projectId, string gameVersion, ModLoader loader, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModFile>>(Array.Empty<ModFile>());
}
