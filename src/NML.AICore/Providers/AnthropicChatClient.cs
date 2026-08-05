using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.AICore.Providers;

/// <summary>
/// Chat client speaking Anthropic's Messages API (<c>POST /v1/messages</c>).
/// Auth is <c>x-api-key</c> + <c>anthropic-version</c>; streaming is SSE where the text
/// arrives in <c>content_block_delta</c> events (<c>delta.text</c>). System prompt is a
/// top-level field rather than a message — we extract it from the conversation.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    public ChatProviderConfig Provider { get; }

    public AnthropicChatClient(HttpClient http, ChatProviderConfig provider)
    {
        _http = http;
        Provider = provider;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Anthropic splits system out of the message list.
        string? system = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Content;
        var convo = messages.Where(m => m.Role != ChatRole.System)
                            .Select(m => new { role = RoleString(m.Role), content = m.Content });

        string url = Provider.BaseUrl.TrimEnd('/') + "/messages";
        var body = new Dictionary<string, object?>
        {
            ["model"] = Provider.Model,
            ["messages"] = convo,
            ["stream"] = true,
            ["max_tokens"] = Provider.MaxOutputTokens == 0 ? 2048 : Provider.MaxOutputTokens,
            ["temperature"] = Provider.Temperature,
        };
        if (!string.IsNullOrEmpty(system)) body["system"] = system;

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("x-api-key", Provider.ApiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:")) continue;

            string data = line["data:".Length..].Trim();
            string? text = ExtractDeltaText(data);
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    private static string? ExtractDeltaText(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return null;
            if (type.GetString() != "content_block_delta") return null;

            return root.GetProperty("delta")
                       .TryGetProperty("text", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    private static string RoleString(ChatRole role) => role switch
    {
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => "user",
    };
}
