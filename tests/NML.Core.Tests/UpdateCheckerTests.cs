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
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult(json));
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
        var checker = new UpdateChecker("o", "r", (_, _) => Task.FromResult(json));
        UpdateInfo? info = await checker.CheckAsync("0.1.0");

        info!.IsNewer.Should().BeFalse();
    }
}
