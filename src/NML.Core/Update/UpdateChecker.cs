using System.Text.Json;
using System.Text.Json.Serialization;

namespace NML.Core.Update;

/// <summary>Information about a new release available for download.</summary>
public sealed class UpdateInfo
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty; // release notes
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>True when this release is newer than the running version.</summary>
    public bool IsNewer { get; init; }
}

/// <summary>
/// Checks GitHub Releases for a newer version of the launcher. Compares the latest release
/// tag against the running version using semantic versioning. Pure logic — the HTTP fetch is
/// injected so tests can stub it.
/// </summary>
public sealed class UpdateChecker
{
    private readonly Func<string, CancellationToken, Task<string>> _fetchJson;

    public string RepoOwner { get; }
    public string RepoName { get; }

    public UpdateChecker(string repoOwner, string repoName, Func<string, CancellationToken, Task<string>> fetchJson)
    {
        RepoOwner = repoOwner;
        RepoName = repoName;
        _fetchJson = fetchJson;
    }

    /// <summary>Fetch the latest release and compare against <paramref name="currentVersion"/>.</summary>
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        string json = await _fetchJson(url, ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        DateTimeOffset published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetDateTimeOffset() : DateTimeOffset.UtcNow;

        bool isNewer = IsVersionNewer(tag, currentVersion);

        return new UpdateInfo
        {
            TagName = tag,
            Name = name,
            HtmlUrl = htmlUrl,
            Body = body,
            PublishedAt = published,
            IsNewer = isNewer,
        };
    }

    /// <summary>
    /// Compare a release tag (e.g. "v0.2.0") against the current version (e.g. "0.1.0").
    /// Uses simple numeric component comparison (major.minor.patch). Returns true if the tag
    /// is strictly newer.
    /// </summary>
    public static bool IsVersionNewer(string releaseTag, string currentVersion)
    {
        var rel = ParseVersion(releaseTag);
        var cur = ParseVersion(currentVersion);

        if (rel.Major != cur.Major) return rel.Major > cur.Major;
        if (rel.Minor != cur.Minor) return rel.Minor > cur.Minor;
        return rel.Patch > cur.Patch;
    }

    /// <summary>Parse a version string like "v0.2.0" or "0.2.0-alpha" into (major, minor, patch).</summary>
    public static (int Major, int Minor, int Patch) ParseVersion(string s)
    {
        // Strip a leading 'v' and any pre-release suffix after '-'.
        string clean = s.TrimStart('v').Split('-')[0];
        var parts = clean.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int ma) ? ma : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int mi) ? mi : 0;
        int patch = parts.Length > 2 && int.TryParse(parts[2], out int pa) ? pa : 0;
        return (major, minor, patch);
    }
}
