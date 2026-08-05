namespace NML.AICore;

/// <summary>
/// Where chat completions should be sent. Cloud providers (OpenAI/Anthropic) require
/// an API key; local providers (Ollama/LM Studio) need none and work offline.
/// </summary>
public enum ChatProviderKind
{
    /// <summary>OpenAI-compatible HTTP API (api.openai.com, or any compatible proxy/local server).</summary>
    OpenAiCompatible,

    /// <summary>Anthropic Messages API (api.anthropic.com).</summary>
    Anthropic,

    /// <summary>A local model server (Ollama on :11434, LM Studio on :1234). No key needed.</summary>
    Local,
}

/// <summary>
/// User-configured AI backend: which provider, its base URL, the model id, and an
/// optional API key. Built in the settings UI and fed to the chat client factory.
/// </summary>
public sealed class ChatProviderConfig
{
    public ChatProviderKind Kind { get; init; } = ChatProviderKind.Local;

    /// <summary>Display name shown in the UI.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Base URL, e.g. <c>https://api.openai.com/v1</c> or <c>http://localhost:11434</c>.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The model id to call, e.g. <c>gpt-4o-mini</c>, <c>claude-3-5-haiku-latest</c>, <c>llama3.1:8b</c>.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>API key (cloud only). Held in memory; persisted encrypted via <c>ISecretStore</c>.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Sampling temperature, 0..2. Defaults to 0.3 for focused/deterministic answers.</summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>Max output tokens per response. 0 = provider default.</summary>
    public int MaxOutputTokens { get; init; } = 2048;
}
