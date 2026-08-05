using NML.AICore.Features;
using NML.AICore.Providers;

namespace NML.AICore.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void Parses_well_formed_json_diagnosis()
    {
        string modelReply = """
            {
              "root_cause": "Fabric API is missing; a mod references net.fabricmc.fabric.api.event.Event.",
              "confidence": "high",
              "likely_fixes": ["Install Fabric API 0.92.x for 1.20.1.", "Update 'somemod' to a build that targets 1.20.1."],
              "affected_mods": ["somemod", "fabric-api"]
            }
            """;

        CrashDiagnosis d = CrashAnalyzer.Parse(modelReply);

        d.RootCause.Should().Contain("Fabric API is missing");
        d.Confidence.Should().Be("high");
        d.LikelyFixes.Should().HaveCount(2);
        d.AffectedMods.Should().Contain("fabric-api");
    }

    [Fact]
    public void Tolerates_code_fenced_json()
    {
        string modelReply = """
            ```json
            {"root_cause": "x", "confidence": "low", "likely_fixes": [], "affected_mods": []}
            ```
            """;

        CrashDiagnosis d = CrashAnalyzer.Parse(modelReply);
        d.RootCause.Should().Be("x");
        d.Confidence.Should().Be("low");
    }

    [Fact]
    public void Degrades_gracefully_on_garbage()
    {
        CrashDiagnosis d = CrashAnalyzer.Parse("this is not JSON at all");
        d.Confidence.Should().Be("low");
        d.LikelyFixes.Should().BeEmpty();
        d.RawNarrative.Should().Be("this is not JSON at all");
    }

    [Fact]
    public async Task End_to_end_with_fake_client_uses_parsed_report()
    {
        const string reply = "{\"root_cause\":\"ok\",\"confidence\":\"high\",\"likely_fixes\":[\"f1\"],\"affected_mods\":[]}";
        var fake = new FakeChatClient(reply);
        var analyzer = new CrashAnalyzer(fake, Microsoft.Extensions.Logging.Abstractions.NullLogger<CrashAnalyzer>.Instance);

        CrashDiagnosis result = await analyzer.AnalyzeAsync(
            "Description: Exception ticking world\njava.lang.Error: boom");

        fake.CallCount.Should().Be(1);
        fake.LastMessages.Should().NotBeNull();
        fake.LastMessages.Should().Contain(m => m.Role == ChatRole.System);
        // The user message must include the parsed description.
        fake.LastMessages.Should().Contain(m =>
            m.Role == ChatRole.User && m.Content.Contains("Exception ticking world"));
        result.RootCause.Should().Be("ok");
        result.Confidence.Should().Be("high");
    }

    [Fact]
    public void BuildUserPrompt_includes_mods_and_system_details()
    {
        var report = new CrashReport
        {
            Description = "boom",
            SystemDetails = "Minecraft Version: 1.20.1",
            Mods = new Dictionary<string, string> { ["sodium"] = "0.5.3" },
            StackTraceHead = "at x.y(Z.java:1)",
        };
        string prompt = CrashAnalyzer.BuildUserPrompt(report);

        prompt.Should().Contain("Description: boom");
        prompt.Should().Contain("sodium:0.5.3");
        prompt.Should().Contain("Minecraft Version: 1.20.1");
        prompt.Should().Contain("at x.y(Z.java:1)");
    }
}
