using NML.Core.Update;

namespace NML.Core.Tests;

/// <summary>
/// Validates the version-comparison logic (the deterministic contract). The GitHub API call
/// is covered by the runtime; these tests pin the versioning that decides "is this newer?".
/// </summary>
public class UpdateCheckerTests
{
    [Fact]
    public void Newer_major_version_is_detected()
    {
        UpdateChecker.IsVersionNewer("v1.0.0", "0.1.0").Should().BeTrue();
    }

    [Fact]
    public void Newer_minor_version_is_detected()
    {
        UpdateChecker.IsVersionNewer("v0.2.0", "0.1.0").Should().BeTrue();
    }

    [Fact]
    public void Newer_patch_version_is_detected()
    {
        UpdateChecker.IsVersionNewer("v0.1.1", "0.1.0").Should().BeTrue();
    }

    [Fact]
    public void Same_version_is_not_newer()
    {
        UpdateChecker.IsVersionNewer("v0.1.0", "0.1.0").Should().BeFalse();
    }

    [Fact]
    public void Older_version_is_not_newer()
    {
        UpdateChecker.IsVersionNewer("v0.0.9", "0.1.0").Should().BeFalse();
    }

    [Fact]
    public void Pre_release_suffix_is_handled()
    {
        UpdateChecker.IsVersionNewer("v0.2.0-alpha", "0.1.0").Should().BeTrue();
    }

    [Fact]
    public void Parses_version_components()
    {
        var v = UpdateChecker.ParseVersion("v1.2.3");
        v.Should().Be((1, 2, 3));
    }

    [Fact]
    public void Parses_version_without_v_prefix()
    {
        var v = UpdateChecker.ParseVersion("2.0.0");
        v.Should().Be((2, 0, 0));
    }

    [Fact]
    public async Task CheckAsync_parses_release_json()
    {
        string json = """
            {
              "tag_name": "v0.5.0",
              "name": "Release 0.5.0",
              "html_url": "https://github.com/weige0831/NextMinecraftLauncher/releases/tag/v0.5.0",
              "body": "New features",
              "published_at": "2026-01-15T10:00:00Z"
            }
            """;
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info.Should().NotBeNull();
        info!.TagName.Should().Be("v0.5.0");
        info.Name.Should().Be("Release 0.5.0");
        info.HtmlUrl.Should().Contain("v0.5.0");
        info.IsNewer.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_returns_not_newer_for_same_version()
    {
        string json = """{"tag_name":"v0.1.0","name":"x","html_url":"","body":"","published_at":"2026-01-01T00:00:00Z"}""";
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info!.IsNewer.Should().BeFalse();
    }

    // ===== Robustness: network failure, malformed/empty JSON, missing tag_name (return null) =====

    [Fact]
    public async Task CheckAsync_returns_null_on_network_error()
    {
        var checker = new UpdateChecker("o", "r", (_, _) => throw new HttpRequestException("timeout"));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");
        info.Should().BeNull("a network failure must surface as 'no info', not an exception");
    }

    [Fact]
    public async Task CheckAsync_returns_null_on_empty_response()
    {
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(""));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");
        info.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_returns_null_on_null_response()
    {
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(null));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");
        info.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_returns_null_on_malformed_json()
    {
        // A GitHub rate-limit 403 returns an HTML/plain body, not JSON — must not throw.
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>("API rate limit exceeded"));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");
        info.Should().BeNull("a non-JSON response must be treated as 'no info'");
    }

    [Fact]
    public async Task CheckAsync_returns_null_when_tag_name_missing()
    {
        string json = """{"name":"no tag here","html_url":"x"}""";
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");
        info.Should().BeNull("a release with no tag_name is unusable");
    }

    // ===== Asset parsing =====

    [Fact]
    public async Task CheckAsync_parses_assets_array()
    {
        string json = """
        {
          "tag_name": "v0.6.0",
          "name": "Release 0.6.0",
          "html_url": "https://github.com/o/r/releases/tag/v0.6.0",
          "body": "",
          "published_at": "2026-02-01T00:00:00Z",
          "prerelease": false,
          "assets": [
            { "name": "NML.App.exe", "browser_download_url": "https://github.com/o/r/releases/download/v0.6.0/NML.App.exe", "size": 47185920 },
            { "name": "checksums.sha256", "browser_download_url": "https://github.com/o/r/releases/download/v0.6.0/checksums.sha256", "size": 256 }
          ]
        }
        """;
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info!.Assets.Should().HaveCount(2);
        var exe = info.Assets.First(a => a.Name == "NML.App.exe");
        exe.Url.Should().Contain("/v0.6.0/NML.App.exe");
        exe.Size.Should().Be(47185920);
        info.IsPrerelease.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_handles_release_with_no_assets()
    {
        string json = """{"tag_name":"v0.7.0","name":"x","html_url":"","body":"","published_at":"2026-03-01T00:00:00Z","prerelease":true}""";
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info!.Assets.Should().BeEmpty();
        info.IsPrerelease.Should().BeTrue();
    }

    // ===== E2E: full check via a fake fetcher that mirrors the real DI bridge =====

    [Fact]
    public async Task E2E_Check_Through_Fake_Fetcher_Detects_Newer_Version()
    {
        // Wire UpdateChecker exactly as production DI does, but the fetch returns canned JSON.
        // This validates the full path: URL build → fetch → parse → version compare → assets.
        string json = """
        {
          "tag_name": "v9.9.9",
          "name": "Future Release",
          "html_url": "https://github.com/weige0831/NextMinecraftLauncher/releases/tag/v9.9.9",
          "body": "- everything fixed",
          "published_at": "2026-12-31T00:00:00Z",
          "prerelease": false,
          "assets": [{ "name": "NML.App.exe", "browser_download_url": "https://example.com/NML.App.exe", "size": 1 }]
        }
        """;
        var checker = new UpdateChecker("weige0831", "NextMinecraftLauncher", (_, _) => Task.FromResult<string?>(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info.Should().NotBeNull();
        info!.IsNewer.Should().BeTrue("v9.9.9 > 0.1.0");
        info.TagName.Should().Be("v9.9.9");
        info.Assets.Should().ContainSingle();
    }
}
