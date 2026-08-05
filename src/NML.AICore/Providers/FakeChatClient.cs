using System.Runtime.CompilerServices;

namespace NML.AICore.Providers;

/// <summary>
/// A scripted chat client for unit tests: yields a fixed sequence of chunks (or the
/// concatenated full reply) when called. Lets features be tested without any network.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly string[] _chunks;

    public ChatProviderConfig Provider { get; }
    public int CallCount { get; private set; }

    /// <summary>Record of the last conversation handed to <see cref="StreamAsync"/>.</summary>
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public FakeChatClient(string reply, ChatProviderConfig? provider = null, int chunkSize = 8)
    {
        Provider = provider ?? new ChatProviderConfig
        {
            Kind = ChatProviderKind.Local,
            Name = "fake",
            BaseUrl = "http://localhost",
            Model = "fake-model",
        };

        // Split the canned reply into fixed-size chunks to simulate streaming.
        var chunks = new List<string>();
        for (int i = 0; i < reply.Length; i += chunkSize)
            chunks.Add(reply[i..Math.Min(i + chunkSize, reply.Length)]);
        _chunks = chunks.Count == 0 ? new[] { string.Empty } : chunks.ToArray();
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        CallCount++;
        LastMessages = messages;
        foreach (string chunk in _chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }
}
