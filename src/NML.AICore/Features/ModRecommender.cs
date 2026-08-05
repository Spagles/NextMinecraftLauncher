using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Data;

namespace NML.AICore.Features;

/// <summary>
/// A single recommendation: a real, verified mod (id/version come from the catalog API, never
/// the model) plus the LLM's explanation of why it fits the user's request.
/// </summary>
public sealed class ModRecommendation
{
    public ModSearchResult Mod { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
    public int Rank { get; init; }
}

/// <summary>
/// AI mod recommender using retrieval-augmented generation to guarantee no hallucinated
/// mod ids. Pipeline:
/// <list type="number">
/// <item><b>Retrieve</b>: search the catalog API (Modrinth/CurseForge) for real candidates
///   matching the request + game version + loader. The candidates carry verified ids.</item>
/// <item><b>Reason</b>: feed the candidate set (with real ids) to the LLM and ask it to
///   rank and explain — constrained to pick only from the provided ids.</item>
/// <item><b>Render</b>: pair each LLM-picked id back to its real <see cref="ModSearchResult"/>
///   so the install step uses verified data.</item>
/// </list>
/// This two-stage design is what kills the hallucination problem pure LLM recommenders have.
/// </summary>
public sealed class ModRecommender
{
    private readonly IChatClient _chat;
    private readonly ILogger<ModRecommender> _logger;

    public ModRecommender(IChatClient chat, ILogger<ModRecommender> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    private const string SystemPrompt = """
        You recommend Minecraft mods to a user. You will receive a user request and a
        CANDIDATE LIST of real mods (each with an id, title, downloads, categories).
        Rank the most relevant candidates for the request and explain each pick in one
        short sentence. You may ONLY recommend mods from the candidate list — never invent
        ids. If no candidate fits, return an empty list.
        Respond as JSON only, no markdown fences, with this exact shape:
        {"picks":[{"id":string,"reason":string}]}
        """;

    /// <summary>
    /// Recommend mods for <paramref name="userRequest"/> using the given catalog.
    /// The catalog search provides verified candidates; the LLM only ranks them.
    /// </summary>
    public async Task<IReadOnlyList<ModRecommendation>> RecommendAsync(
        IModCatalog catalog,
        string userRequest,
        string? gameVersion = null,
        ModLoader loader = ModLoader.Any,
        int maxResults = 5,
        CancellationToken ct = default)
    {
        // Stage 1: retrieve real candidates from the catalog.
        // Derive search keywords from the request by stripping filler words.
        string query = DeriveSearchQuery(userRequest);
        IReadOnlyList<ModSearchResult> candidates = await catalog.SearchAsync(
            query, gameVersion, loader == ModLoader.Any ? null : loader,
            limit: 25, ct);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Recommender: no catalog candidates for '{Query}'.", query);
            return Array.Empty<ModRecommendation>();
        }

        // Stage 2: ask the LLM to rank the candidates (constrained to their real ids).
        string userPrompt = BuildPrompt(userRequest, candidates);
        string raw = await _chat.CompleteAsync(new[]
        {
            new ChatMessage { Role = ChatRole.System, Content = SystemPrompt },
            new ChatMessage { Role = ChatRole.User, Content = userPrompt },
        }, ct);

        // Stage 3: pair LLM picks back to real ModSearchResults.
        IReadOnlyList<ModRecommendation> picks = Map(raw, candidates, maxResults);
        _logger.LogInformation("Recommender: {Picks} picks from {Candidates} candidates.",
            picks.Count, candidates.Count);
        return picks;
    }

    /// <summary>Build the user prompt listing real candidates with their verified ids.</summary>
    public static string BuildPrompt(string userRequest, IReadOnlyList<ModSearchResult> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"User request: {userRequest}");
        sb.AppendLine();
        sb.AppendLine("Candidate mods (real, verified):");
        foreach (ModSearchResult c in candidates)
        {
            sb.AppendLine($"- id={c.ProjectId} | {c.Title} by {c.Author} | {c.Downloads} downloads | categories: {string.Join("/", c.Categories)}");
            if (!string.IsNullOrEmpty(c.Description))
                sb.AppendLine($"    {Truncate(c.Description, 120)}");
        }
        sb.AppendLine();
        sb.AppendLine("Rank the best fits. Output JSON only.");
        return sb.ToString();
    }

    /// <summary>Map the model's JSON picks back to real candidates by id (drops anything not in the list).</summary>
    public static IReadOnlyList<ModRecommendation> Map(
        string rawModelOutput, IReadOnlyList<ModSearchResult> candidates, int maxResults)
    {
        string trimmed = StripCodeFences(rawModelOutput).Trim();
        var byId = candidates.ToDictionary(c => c.ProjectId, c => c, StringComparer.OrdinalIgnoreCase);
        var picks = new List<ModRecommendation>();

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("picks", out var arr)) return picks;
            foreach (var p in arr.EnumerateArray())
            {
                string? id = p.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String
                    ? i.GetString() : null;
                string reason = p.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? string.Empty : string.Empty;
                if (id is null || !byId.TryGetValue(id, out ModSearchResult? mod)) continue;
                picks.Add(new ModRecommendation { Mod = mod, Reason = reason, Rank = picks.Count + 1 });
                if (picks.Count >= maxResults) break;
            }
        }
        catch (JsonException) { /* degrade to empty */ }
        return picks;
    }

    /// <summary>Reduce a free-text request to search keywords (drop common stop words).</summary>
    public static string DeriveSearchQuery(string request)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "the", "for", "me", "i", "want", "need", "like", "mod", "mods",
          "please", "find", "show", "give", "with", "that", "which", "to", "and", "of", "some" };
        var words = request.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => !stop.Contains(w) && w.Length > 1);
        return string.Join(' ', words);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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
