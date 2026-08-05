using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.AICore.Providers;

/// <summary>
/// Chat client speaking the OpenAI <c>/chat/completions</c> SSE protocol. Covers three
/// cases from a single implementation:
/// <list type="bullet">
/// <item><b>OpenAI cloud</b> — <c>https://api.openai.com/v1</c> + Bearer key.</item>
/// <item><b>Ollama local</b> — <c>http://localhost:11434/v1</c> (its OpenAI-compat endpoint), no key.</item>
/// <item><b>LM Studio / any OpenAI-compat server</b> — base URL + optional key.</item>
/// </list>
/// Streaming uses <c>stream: true</c> + Server-Sent-Events; each <c>data:</c> line carries a
/// JSON chunk whose <c>choices[0].delta.content</c> is the incremental text.
/// </summary>
public sealed class OpenAiCompatibleChatClient : IChatClient
{
    private readonly HttpClient _http;

    public ChatProviderConfig Provider { get; }

    public OpenAiCompatibleChatClient(HttpClient http, ChatProviderConfig provider)
    {
        _http = http;
        Provider = provider;

        if (!string.IsNullOrEmpty(provider.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", provider.ApiKey);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string url = Provider.BaseUrl.TrimEnd('/') + "/chat/completions";

        var body = new
        {
            model = Provider.Model,
            messages = messages.Select(m => new { role = RoleString(m.Role), content = m.Content }),
            stream = true,
            temperature = Provider.Temperature,
            max_tokens = Provider.MaxOutputTokens == 0 ? (object?)null : Provider.MaxOutputTokens,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        using var resp = await _http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, ct);
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
            if (data == "[DONE]") break;

            string? delta = ExtractDelta(data);
            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }

    private static string? ExtractDelta(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .TryGetProperty("content", out var c) ? c.GetString() : null;
        }
        catch { return null; }
    }

    private static string RoleString(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        ChatRole.Tool => "tool",
        _ => "user",
    };
}
