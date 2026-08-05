using NML.AICore.Providers;
using NML.AICore.Tools;

namespace NML.AICore.Tests;

public class NaturalConfigAgentTests
{
    [Fact]
    public void Parses_single_tool_call()
    {
        string modelReply = """
            {
              "explanation": "Setting 6 GB of RAM for 1.20.1.",
              "calls": [
                { "tool": "set_memory", "arguments": { "min_mb": 2048, "max_mb": 6144 } }
              ]
            }
            """;

        ConfigProposal proposal = NaturalConfigAgent.Parse(modelReply);

        proposal.Explanation.Should().Contain("6 GB");
        proposal.Calls.Should().ContainSingle();
        proposal.Calls[0].Tool.Should().Be("set_memory");
        proposal.Calls[0].Arguments["max_mb"].GetInt32().Should().Be(6144);
    }

    [Fact]
    public void Parses_multiple_tool_calls()
    {
        string modelReply = """
            {
              "explanation": "Smooth 1.20.1 setup.",
              "calls": [
                { "tool": "set_memory", "arguments": { "min_mb": 2048, "max_mb": 8192 } },
                { "tool": "set_minecraft_version", "arguments": { "version_id": "1.20.1" } },
                { "tool": "set_modloader", "arguments": { "loader": "fabric" } }
              ]
            }
            """;

        ConfigProposal proposal = NaturalConfigAgent.Parse(modelReply);
        proposal.Calls.Should().HaveCount(3);
        proposal.Calls.Select(c => c.Tool)
            .Should().BeEquivalentTo(new[] { "set_memory", "set_minecraft_version", "set_modloader" });
    }

    [Fact]
    public void Handles_zero_calls_when_unclear()
    {
        string modelReply = """
            { "explanation": "I need to know which version you want.", "calls": [] }
            """;

        ConfigProposal proposal = NaturalConfigAgent.Parse(modelReply);
        proposal.Calls.Should().BeEmpty();
        proposal.Explanation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Degrades_gracefully_on_garbage()
    {
        ConfigProposal proposal = NaturalConfigAgent.Parse("not json");
        proposal.Calls.Should().BeEmpty();
        proposal.Raw.Should().Be("not json");
    }

    [Fact]
    public async Task End_to_end_with_fake_client()
    {
        const string reply = """
            { "explanation": "ok", "calls": [{ "tool": "set_java_runtime", "arguments": { "major_version": 17 } }] }
            """;
        var fake = new FakeChatClient(reply);
        var agent = new NaturalConfigAgent(fake, Microsoft.Extensions.Logging.Abstractions.NullLogger<NaturalConfigAgent>.Instance);

        ConfigProposal proposal = await agent.ProposeAsync("use java 17");
        fake.CallCount.Should().Be(1);
        fake.LastMessages.Should().NotBeNull();
        // The system prompt must enumerate the available tools.
        fake.LastMessages.Should().Contain(m =>
            m.Role == ChatRole.System && m.Content.Contains("set_memory"));
        proposal.Calls.Should().ContainSingle();
        proposal.Calls[0].Arguments["major_version"].GetInt32().Should().Be(17);
    }
}
