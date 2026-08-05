using NML.AICore.Providers;

namespace NML.AICore;

/// <summary>
/// Builds an <see cref="IChatClient"/> for a given <see cref="ChatProviderConfig"/>,
/// selecting the right wire protocol (OpenAI-compatible SSE vs Anthropic Messages).
/// Uses a fresh <c>HttpClient</c> per client so each provider's base URL + headers are isolated.
/// </summary>
public sealed class ChatClientFactory
{
    private readonly Func<ChatProviderConfig, HttpClient> _httpClientFactory;

    public ChatClientFactory(Func<ChatProviderConfig, HttpClient>? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? (cfg => new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) });
    }

    public IChatClient Create(ChatProviderConfig provider)
    {
        Validate(provider);

        HttpClient http = _httpClientFactory(provider);
        if (!string.IsNullOrEmpty(provider.BaseUrl) && http.BaseAddress is null)
            http.BaseAddress = new Uri(provider.BaseUrl);

        // A short timeout protects against unresponsive local servers being silently stuck.
        http.Timeout = TimeSpan.FromMinutes(10);

        return provider.Kind switch
        {
            ChatProviderKind.OpenAiCompatible => new OpenAiCompatibleChatClient(http, provider),
            ChatProviderKind.Anthropic => new AnthropicChatClient(http, provider),
            // Local servers (Ollama/LM Studio) speak the OpenAI-compatible protocol.
            ChatProviderKind.Local => new OpenAiCompatibleChatClient(http, provider),
            _ => throw new ArgumentOutOfRangeException(nameof(provider.Kind)),
        };
    }

    private static void Validate(ChatProviderConfig p)
    {
        if (string.IsNullOrWhiteSpace(p.BaseUrl))
            throw new ArgumentException("Provider BaseUrl is required.", nameof(p));
        if (string.IsNullOrWhiteSpace(p.Model))
            throw new ArgumentException("Provider Model is required.", nameof(p));
        if (p.Kind is ChatProviderKind.OpenAiCompatible or ChatProviderKind.Anthropic
            && string.IsNullOrWhiteSpace(p.ApiKey))
        {
            throw new ArgumentException("Cloud providers require an API key.", nameof(p));
        }
    }
}
