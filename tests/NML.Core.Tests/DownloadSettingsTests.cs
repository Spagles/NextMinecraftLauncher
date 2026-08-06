using NML.Core.Download;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the download-tuning surface behind PCL-style settings: <see cref="DownloadSettings"/>
/// clamps concurrency to the 1–64 safe range, and <see cref="MirrorUrlRewriter"/> reroutes the
/// known Mojang asset/library hosts through a mirror (BMCLAPI-style) while leaving everything
/// else untouched. Both are pure + unit-tested off the network.
/// </summary>
public class DownloadSettingsTests
{
    [Theory]
    [InlineData(0, 1)]     // below floor → clamped to min
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(64, 64)]
    [InlineData(65, 64)]   // above ceiling → clamped to max
    [InlineData(1000, 64)]
    public void Concurrency_Is_Clamped_To_1_64(int input, int expected)
    {
        var s = new DownloadSettings { Concurrency = input };
        s.Concurrency.Should().Be(expected);
    }

    [Fact]
    public void Clamp_Static_Helpers_Match_Bounds()
    {
        DownloadSettings.Clamp(0).Should().Be(DownloadSettings.MinConcurrency);
        DownloadSettings.Clamp(64).Should().Be(DownloadSettings.MaxConcurrency);
        DownloadSettings.Clamp(32).Should().Be(32);
    }

    [Fact]
    public void Defaults_Are_Sane()
    {
        var s = new DownloadSettings();
        s.Concurrency.Should().Be(DownloadSettings.DefaultConcurrency);
        s.HasMirror.Should().BeFalse(); // no mirror by default → official endpoints
    }

    [Fact]
    public void HasMirror_Reflects_MirrorUrl()
    {
        new DownloadSettings { MirrorUrl = "https://bmclapi2.bangbang93.com" }.HasMirror.Should().BeTrue();
        new DownloadSettings { MirrorUrl = "   " }.HasMirror.Should().BeFalse();
        new DownloadSettings { MirrorUrl = null }.HasMirror.Should().BeFalse();
    }
}

public class MirrorUrlRewriterTests
{
    private const string Mirror = "https://bmclapi2.bangbang93.com";

    [Theory]
    // Each known Mojang host on https is rewritten to the mirror.
    [InlineData("https://libraries.minecraft.net/com/foo/bar.jar",
                "https://bmclapi2.bangbang93.com/com/foo/bar.jar")]
    [InlineData("https://piston-data.mojang.com/v1/abc/client.jar",
                "https://bmclapi2.bangbang93.com/v1/abc/client.jar")]
    [InlineData("https://piston-meta.mojang.com/v1/manifest.json",
                "https://bmclapi2.bangbang93.com/v1/manifest.json")]
    [InlineData("https://assets.minecraft.net/ab/hash",
                "https://bmclapi2.bangbang93.com/ab/hash")]
    [InlineData("https://resources.download.minecraft.net/ab/hash",
                "https://bmclapi2.bangbang93.com/ab/hash")]
    // http:// is also rewritten (not just https).
    [InlineData("http://libraries.minecraft.net/x/y.jar",
                "https://bmclapi2.bangbang93.com/x/y.jar")]
    public void Rewrite_Reroutes_Known_Mojang_Hosts_To_Mirror(string input, string expected)
    {
        MirrorUrlRewriter.Rewrite(input, Mirror).Should().Be(expected);
    }

    [Fact]
    public void Rewrite_Preserves_Non_Mojang_Urls()
    {
        // A non-Mojang URL is returned verbatim — we must not silently reroute arbitrary hosts.
        MirrorUrlRewriter.Rewrite("https://example.com/foo.jar", Mirror)
            .Should().Be("https://example.com/foo.jar");
        MirrorUrlRewriter.Rewrite("https://modrinth.com/data/mod.jar", Mirror)
            .Should().Be("https://modrinth.com/data/mod.jar");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rewrite_Disabled_When_No_Mirror(string? mirror)
    {
        // No mirror configured → every URL passes through untouched (official endpoints).
        const string url = "https://libraries.minecraft.net/x.jar";
        MirrorUrlRewriter.Rewrite(url, mirror).Should().Be(url);
    }

    [Fact]
    public void Rewrite_Handles_Trailing_Slash_On_Mirror()
    {
        // A user-typed mirror with a trailing slash must not double-slash the result.
        MirrorUrlRewriter.Rewrite("https://libraries.minecraft.net/x.jar", Mirror + "/")
            .Should().Be("https://bmclapi2.bangbang93.com/x.jar");
    }

    [Fact]
    public void Rewrite_Is_Case_Insensitive_On_Host()
    {
        MirrorUrlRewriter.Rewrite("HTTPS://LIBRARIES.MINECRAFT.NET/X.jar", Mirror)
            .Should().Be("https://bmclapi2.bangbang93.com/X.jar");
    }

    [Fact]
    public void RewriteAll_Applies_To_Every_Url()
    {
        var urls = new[]
        {
            "https://libraries.minecraft.net/a.jar",
            "https://example.com/b.jar", // non-Mojang, untouched
        };
        var rewritten = MirrorUrlRewriter.RewriteAll(urls, Mirror).ToList();
        rewritten.Should().Equal(
            "https://bmclapi2.bangbang93.com/a.jar",
            "https://example.com/b.jar");
    }
}
