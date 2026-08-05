using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NML.AICore.Tools;

/// <summary>
/// Translates a natural-language request ("give me a smooth 1.20.1 setup") into a set of
/// concrete launcher tool calls. The model is constrained to a small, locally-defined tool
/// surface (set_memory, set_version, ...) and must emit JSON. The launcher applies the
/// returned calls after explicit user confirmation — the agent never executes anything itself.
/// </summary>
public sealed class NaturalConfigAgent
{
    private readonly IChatClient _chat;
    private readonly ILogger<NaturalConfigAgent> _logger;

    public NaturalConfigAgent(IChatClient chat, ILogger<NaturalConfigAgent> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    private string SystemPrompt =>
        "You configure a Minecraft launcher from a user's natural-language request.\n" +
        "You may ONLY propose changes by emitting tool calls. Available tools:\n" +
        FormatTools() + "\n" +
        "If the request is unclear or you lack enough info, emit zero calls and explain in \"explanation\". " +
        "Otherwise emit one or more calls.\n" +
        "Respond as JSON only, no markdown fences, with this exact shape:\n" +
        "{\"explanation\": string, \"calls\": [{\"tool\": \"<name>\", \"arguments\": {...}}]}";

    /// <summary>Propose tool calls for a user's request. Caller confirms before applying.</summary>
    public async Task<ConfigProposal> ProposeAsync(string userRequest, CancellationToken ct = default)
    {
        _logger.LogInformation("Natural-config proposal for: {Request}", userRequest);

        string raw = await _chat.CompleteAsync(new[]
        {
            new ChatMessage { Role = ChatRole.System, Content = SystemPrompt },
            new ChatMessage { Role = ChatRole.User, Content = userRequest },
        }, ct);

        return Parse(raw);
    }

    /// <summary>Parse the model's JSON into a <see cref="ConfigProposal"/>.</summary>
    public static ConfigProposal Parse(string rawModelOutput)
    {
        string trimmed = StripCodeFences(rawModelOutput).Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            JsonElement root = doc.RootElement;

            string explanation = root.TryGetProperty("explanation", out var ex) && ex.ValueKind == JsonValueKind.String
                ? ex.GetString() ?? string.Empty
                : string.Empty;

            var calls = new List<ToolCall>();
            if (root.TryGetProperty("calls", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in arr.EnumerateArray())
                {
                    if (!c.TryGetProperty("tool", out var t) || t.ValueKind != JsonValueKind.String) continue;
                    var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (c.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in a.EnumerateObject())
                            args[p.Name] = p.Value.Clone();
                    }
                    calls.Add(new ToolCall { Tool = t.GetString() ?? string.Empty, Arguments = args });
                }
            }
            return new ConfigProposal { Explanation = explanation, Calls = calls, Raw = rawModelOutput };
        }
        catch (JsonException)
        {
            return new ConfigProposal
            {
                Explanation = "The model's reply wasn't parseable; showing it verbatim.",
                Calls = new List<ToolCall>(),
                Raw = rawModelOutput,
            };
        }
    }

    private string FormatTools()
    {
        var sb = new System.Text.StringBuilder();
        foreach (AgentTool t in LauncherTools.All)
        {
            sb.AppendLine($"- {t.Name}: {t.Description}");
            foreach ((string pname, ToolParameter p) in t.Parameters)
            {
                string req = p.Required ? "required" : "optional";
                sb.AppendLine($"    arg {pname} ({p.Type}, {req}): {p.Description}");
            }
        }
        return sb.ToString();
    }

    private static string StripCodeFences(string s)
    {
        if (s.StartsWith("```"))
        {
            int firstNl = s.IndexOf('\n');
            if (firstNl > 0) s = s[(firstNl + 1)..];
            int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) s = s[..lastFence];
        }
        return s;
    }
}

/// <summary>Result of <see cref="NaturalConfigAgent.ProposeAsync"/>: explanation + proposed calls.</summary>
public sealed class ConfigProposal
{
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<ToolCall> Calls { get; init; } = Array.Empty<ToolCall>();
    public string Raw { get; init; } = string.Empty;
}
