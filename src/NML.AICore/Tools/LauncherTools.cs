using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.AICore.Tools;

/// <summary>A tool the natural-language agent can invoke. Maps 1:1 to a launcher method.</summary>
public sealed class AgentTool
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>JSON-Schema-ish parameter descriptor the model fills in.</summary>
    public IReadOnlyDictionary<string, ToolParameter> Parameters { get; init; }
        = new Dictionary<string, ToolParameter>();
}

public sealed class ToolParameter
{
    public string Type { get; init; } = "string";
    public string Description { get; init; } = string.Empty;
    public bool Required { get; init; }
    public IReadOnlyList<string>? Enum { get; init; }
}

/// <summary>
/// The agent's requested action: a named tool call with arguments already parsed.
/// The launcher executes these locally (after user confirmation) and feeds results back.
/// </summary>
public sealed class ToolCall
{
    public string Tool { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; }
        = new Dictionary<string, JsonElement>();
}

/// <summary>
/// Registry of the tools the natural-language agent may call. The agent's LLM prompt
/// describes these; the agent parses the model's reply into <see cref="ToolCall"/>s.
/// Keeping them in one place makes the tool surface auditable and easy to extend.
/// </summary>
public static class LauncherTools
{
    /// <summary>All tools the agent can propose. Names match the LLM's tool names.</summary>
    public static readonly IReadOnlyList<AgentTool> All = new[]
    {
        new AgentTool
        {
            Name = "set_memory",
            Description = "Set the Minecraft min/max heap in megabytes.",
            Parameters = new Dictionary<string, ToolParameter>
            {
                ["min_mb"] = new() { Type = "integer", Description = "Minimum heap (Xms) in MB.", Required = true },
                ["max_mb"] = new() { Type = "integer", Description = "Maximum heap (Xmx) in MB.", Required = true },
            },
        },
        new AgentTool
        {
            Name = "set_minecraft_version",
            Description = "Choose which Minecraft version an instance uses.",
            Parameters = new Dictionary<string, ToolParameter>
            {
                ["version_id"] = new() { Type = "string", Description = "e.g. 1.20.1, 1.19.2.", Required = true },
            },
        },
        new AgentTool
        {
            Name = "set_modloader",
            Description = "Install/select a modloader for an instance.",
            Parameters = new Dictionary<string, ToolParameter>
            {
                ["loader"] = new() { Type = "string", Description = "Which modloader.", Enum = new[] { "fabric", "quilt", "forge", "none" }, Required = true },
                ["version"] = new() { Type = "string", Description = "Loader version; omit for latest." },
            },
        },
        new AgentTool
        {
            Name = "set_java_runtime",
            Description = "Pick the Java runtime major version to launch with.",
            Parameters = new Dictionary<string, ToolParameter>
            {
                ["major_version"] = new() { Type = "integer", Description = "8, 17, 21, etc.", Required = true },
            },
        },
        new AgentTool
        {
            Name = "set_resolution",
            Description = "Set the game window resolution.",
            Parameters = new Dictionary<string, ToolParameter>
            {
                ["width"] = new() { Type = "integer", Required = true },
                ["height"] = new() { Type = "integer", Required = true },
            },
        },
    };
}
