using NML.AICore;
using NML.AICore.Providers;

namespace NML.AICore.Tests;

public class ChatClientFactoryTests
{
    private static ChatProviderConfig Local() => new()
    {
        Kind = ChatProviderKind.Local,
        Name = "ollama",
        BaseUrl = "http://localhost:11434/v1",
        Model = "llama3.1:8b",
    };

    private static ChatProviderConfig OpenAi() => new()
    {
        Kind = ChatProviderKind.OpenAiCompatible,
        Name = "openai",
        BaseUrl = "https://api.openai.com/v1",
        Model = "gpt-4o-mini",
        ApiKey = "sk-test",
    };

    [Fact]
    public void Builds_openai_compatible_client_for_local()
    {
        var factory = new ChatClientFactory(_ => new HttpClient());
        IChatClient client = factory.Create(Local());
        client.Should().BeOfType<OpenAiCompatibleChatClient>();
    }

    [Fact]
    public void Builds_openai_compatible_client_for_openai()
    {
        var factory = new ChatClientFactory(_ => new HttpClient());
        IChatClient client = factory.Create(OpenAi());
        client.Should().BeOfType<OpenAiCompatibleChatClient>();
    }

    [Fact]
    public void Builds_anthropic_client_for_anthropic()
    {
        var cfg = new ChatProviderConfig
        {
            Kind = ChatProviderKind.Anthropic,
            Name = "claude",
            BaseUrl = "https://api.anthropic.com/v1",
            Model = "claude-3-5-haiku-latest",
            ApiKey = "sk-ant-test",
        };
        var factory = new ChatClientFactory(_ => new HttpClient());
        IChatClient client = factory.Create(cfg);
        client.Should().BeOfType<AnthropicChatClient>();
    }

    [Fact]
    public void Rejects_missing_base_url()
    {
        var factory = new ChatClientFactory(_ => new HttpClient());
        Action act = () => factory.Create(new ChatProviderConfig { Model = "x" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_cloud_provider_without_api_key()
    {
        var factory = new ChatClientFactory(_ => new HttpClient());
        var cfg = new ChatProviderConfig
        {
            Kind = ChatProviderKind.OpenAiCompatible,
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o",
        };
        Action act = () => factory.Create(cfg);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Local_provider_does_not_require_api_key()
    {
        var factory = new ChatClientFactory(_ => new HttpClient());
        Action act = () => factory.Create(Local());
        act.Should().NotThrow();
    }
}
