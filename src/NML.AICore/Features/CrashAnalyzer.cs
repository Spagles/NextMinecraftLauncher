using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NML.AICore.Features;

/// <summary>
/// Structured output of <see cref="CrashAnalyzer.AnalyzeAsync"/>: the LLM's diagnosis
/// rendered as actionable data so the UI can show fix cards (not just a wall of text).
/// </summary>
public sealed class CrashDiagnosis
{
    public string RootCause { get; init; } = string.Empty;
    public string Confidence { get; init; } = "medium"; // low|medium|high
    public IReadOnlyList<string> LikelyFixes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AffectedMods { get; init; } = Array.Empty<string>();
    public string RawNarrative { get; init; } = string.Empty;
}

/// <summary>
/// Diagnoses a Minecraft crash: parses the report into a tight summary, sends it to the
/// configured <see cref="IChatClient"/> with a focused system prompt, and parses the
/// model's structured-JSON answer into a <see cref="CrashDiagnosis"/>.
/// </summary>
public sealed class CrashAnalyzer
{
    private readonly IChatClient _chat;
    private readonly ILogger<CrashAnalyzer> _logger;

    public CrashAnalyzer(IChatClient chat, ILogger<CrashAnalyzer> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    private const string SystemPrompt = """
        You are a Minecraft Java Edition crash analyst. The user will give you a parsed
        crash report (description, stack head, caused-by chain, system details, mod list
        and a log tail). Diagnose the most likely root cause and give concrete, ordered
        fixes. Be specific: name the exact mod/version/Java mismatch when present.
        Prefer version-incompatibility, Java-version, and mod-conflict causes over
        generic advice. Keep fixes short and actionable.
        Respond as JSON only, no markdown fences, with this exact shape:
        {"root_cause": string, "confidence": "low"|"medium"|"high", "likely_fixes": string[], "affected_mods": string[]}
        """;

    /// <summary>
    /// Analyze a raw crash report (and optional lastest.log tail). Throws if the model
    /// cannot be reached or returns unparseable output (caller shows a fallback message).
    /// </summary>
    public async Task<CrashDiagnosis> AnalyzeAsync(
        string rawCrashReport,
        string? logTail = null,
        CancellationToken ct = default)
    {
        CrashReport report = CrashReportParser.Parse(rawCrashReport, logTail);
        string userPrompt = BuildUserPrompt(report);

        _logger.LogInformation("Analyzing crash {Hash} ({Mods} mods, {Bytes} bytes summarized).",
            report.SourceHash, report.Mods.Count, userPrompt.Length);

        string raw = await _chat.CompleteAsync(new[]
        {
            new ChatMessage { Role = ChatRole.System, Content = SystemPrompt },
            new ChatMessage { Role = ChatRole.User, Content = userPrompt },
        }, ct);

        return Parse(raw);
    }

    /// <summary>Build the tight user-facing prompt from a parsed report.</summary>
    public static string BuildUserPrompt(CrashReport r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Diagnose this Minecraft crash. Output JSON only.");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(r.Description)) sb.AppendLine($"Description: {r.Description}");
        if (r.Mods.Count > 0)
        {
            sb.AppendLine("Mods (" + r.Mods.Count + "):");
            foreach ((string id, string ver) in r.Mods.Take(40))
                sb.AppendLine($"  {id}:{ver}");
            if (r.Mods.Count > 40) sb.AppendLine($"  …({r.Mods.Count - 40} more)");
        }
        if (!string.IsNullOrEmpty(r.SystemDetails))
        {
            sb.AppendLine();
            sb.AppendLine("System Details:");
            sb.AppendLine(r.SystemDetails);
        }
        if (!string.IsNullOrEmpty(r.StackTraceHead))
        {
            sb.AppendLine();
            sb.AppendLine("Stack (head):");
            sb.AppendLine(r.StackTraceHead);
        }
        if (!string.IsNullOrEmpty(r.CausedBy))
        {
            sb.AppendLine();
            sb.AppendLine("Caused by:");
            sb.AppendLine(r.CausedBy);
        }
        if (!string.IsNullOrEmpty(r.LogTail))
        {
            sb.AppendLine();
            sb.AppendLine("Log tail:");
            sb.AppendLine(r.LogTail);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parse the model's JSON answer into a <see cref="CrashDiagnosis"/>. Tolerant: if
    /// JSON parsing fails, fall back to wrapping the raw text as the narrative so the UI
    /// still shows something useful.
    /// </summary>
    public static CrashDiagnosis Parse(string rawModelOutput)
    {
        string trimmed = StripCodeFences(rawModelOutput).Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            JsonElement root = doc.RootElement;
            return new CrashDiagnosis
            {
                RootCause = GetString(root, "root_cause"),
                Confidence = GetString(root, "confidence") is { Length: > 0 } c ? c : "medium",
                LikelyFixes = GetStringList(root, "likely_fixes"),
                AffectedMods = GetStringList(root, "affected_mods"),
                RawNarrative = rawModelOutput,
            };
        }
        catch (JsonException)
        {
            // The model didn't follow the JSON contract — degrade gracefully.
            return new CrashDiagnosis
            {
                RootCause = "Unable to parse the model's structured answer.",
                Confidence = "low",
                LikelyFixes = Array.Empty<string>(),
                AffectedMods = Array.Empty<string>(),
                RawNarrative = rawModelOutput,
            };
        }
    }

    private static string StripCodeFences(string s)
    {
        // Some models wrap JSON in ```json … ``` despite the instruction; strip it.
        if (s.StartsWith("```"))
        {
            int firstNl = s.IndexOf('\n');
            if (firstNl > 0) s = s[(firstNl + 1)..];
            int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) s = s[..lastFence];
        }
        return s;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> GetStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? string.Empty);
        return list;
    }
}
