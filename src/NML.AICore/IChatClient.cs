namespace NML.AICore;

/// <summary>A single chat message in a conversation.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }
    public string Content { get; init; } = string.Empty;
}

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>
/// Provider-agnostic streaming chat client. Each token chunk is yielded via
/// <see cref="IAsyncEnumerable{T}"/> so the UI can render progressively. Implementations
/// wrap OpenAI/Anthropic/Ollama over a single configurable <c>HttpClient</c>.
/// </summary>
public interface IChatClient
{
    /// <summary>The provider this client is configured against.</summary>
    ChatProviderConfig Provider { get; }

    /// <summary>
    /// Stream a completion for the given conversation, yielding text chunks as they arrive.
    /// </summary>
    /// <param name="messages">Full conversation (system + user + assistant turns).</param>
    /// <param name="ct">Cancellation (UI "stop generating" button).</param>
    /// <returns>Each yielded string is an incremental output fragment.</returns>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Non-streaming completion returning the full response. Convenience wrapper over
    /// <see cref="StreamAsync"/> for features that need the whole answer (e.g. JSON tool calls).
    /// </summary>
    async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (string chunk in StreamAsync(messages, ct))
            sb.Append(chunk);
        return sb.ToString();
    }
}
