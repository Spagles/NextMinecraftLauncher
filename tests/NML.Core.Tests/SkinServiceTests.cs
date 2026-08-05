using NML.Core.Skins;

namespace NML.Core.Tests;

/// <summary>
/// Validates the skin-URL builder. These URLs are the contract the UI binds to (Image source),
/// so getting them exactly right matters — a wrong URL renders nothing.
/// </summary>
public class SkinServiceTests
{
    private const string OnlineUuid = "853c80ef-3c37-49fd-aa49-938b674adae6"; // jeb_
    private const string OnlineNoDash = "853c80ef3c3749fdaa49938b674adae6";
    // A real offline (v3 MD5) UUID: version nibble at index 12 must be '3'.
    // Constructed so chars 0-11 are arbitrary and char 12 is '3'.
    private const string OfflineUuid = "660a28a1bc3d349fdaa49938b674adae6";

    [Fact]
    public void Avatar_url_has_correct_shape()
    {
        var svc = new SkinService();
        svc.AvatarUrl(OnlineUuid, 64)
            .Should().Be("https://crafatar.com/avatars/853c80ef3c3749fdaa49938b674adae6?size=64&overlay");
    }

    [Fact]
    public void Avatar_url_accepts_no_dash_uuid()
    {
        var svc = new SkinService();
        svc.AvatarUrl(OnlineNoDash).Should().Contain(OnlineNoDash);
    }

    [Fact]
    public void Head_render_url_uses_renders_head()
    {
        var svc = new SkinService();
        svc.HeadRenderUrl(OnlineUuid, scale: 8)
            .Should().Be("https://crafatar.com/renders/head/853c80ef3c3749fdaa49938b674adae6?scale=8&overlay");
    }

    [Fact]
    public void Body_render_url_uses_renders_body()
    {
        var svc = new SkinService();
        svc.BodyRenderUrl(OnlineUuid).Should().StartWith("https://crafatar.com/renders/body/");
    }

    [Fact]
    public void Skin_texture_url_uses_skins_endpoint()
    {
        var svc = new SkinService();
        svc.SkinTextureUrl(OnlineUuid).Should().Be("https://crafatar.com/skins/853c80ef3c3749fdaa49938b674adae6");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(512)]
    public void Size_is_clamped_to_valid_range(int size)
    {
        var svc = new SkinService();
        string url = svc.AvatarUrl(OnlineUuid, size);
        url.Should().Contain($"size={size}");
    }

    [Fact]
    public void Size_above_max_is_clamped_to_512()
    {
        new SkinService().AvatarUrl(OnlineUuid, 9999).Should().Contain("size=512");
    }

    [Fact]
    public void Normalize_strips_dashes_and_lowercases()
    {
        SkinService.Normalize("AA-BB-CC").Should().Be("aabbcc");
    }

    [Fact]
    public void Offline_uuid_detected_by_version_nibble()
    {
        // v3 (MD5/offline) → version nibble is '3' → offline.
        SkinService.IsLikelyOfflineUuid(OfflineUuid).Should().BeTrue();
        SkinService.IsLikelyOfflineUuid("660a28a1-bc3d-349f-aa49-938b674adae6").Should().BeTrue();
    }

    [Fact]
    public void Online_uuid_not_flagged_offline()
    {
        SkinService.IsLikelyOfflineUuid(OnlineUuid).Should().BeFalse();
    }

    [Fact]
    public void Empty_uuid_throws()
    {
        Action act = () => new SkinService().AvatarUrl("");
        act.Should().Throw<ArgumentException>();
    }
}
