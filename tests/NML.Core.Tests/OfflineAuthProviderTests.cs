using NML.Core.Auth;

namespace NML.Core.Tests;

public class OfflineAuthProviderTests
{
    [Fact]
    public void Produces_a_deterministic_offline_uuid()
    {
        var provider = new OfflineAuthProvider();
        Account a = provider.Create("Notch");
        Account b = provider.Create("Notch");

        a.Uuid.Should().Be(b.Uuid, "offline UUIDs must be deterministic per username");
        a.Username.Should().Be("Notch");
        a.IsOffline.Should().BeTrue();
        a.AccountType.Should().Be("legacy");
        a.Uuid.Should().HaveLength(32, "UUIDs are stored without dashes");
        a.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Different_usernames_yield_different_uuids()
    {
        var provider = new OfflineAuthProvider();
        Account a = provider.Create("Alice");
        Account b = provider.Create("Bob");

        a.Uuid.Should().NotBe(b.Uuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_username(string username)
    {
        var provider = new OfflineAuthProvider();
        Action act = () => provider.Create(username);
        act.Should().Throw<ArgumentException>();
    }

    // ===== Custom UUID (HMCL parity: user can set a fixed UUID for offline accounts) =====

    [Fact]
    public void Custom_bare_uuid_is_honored()
    {
        var provider = new OfflineAuthProvider();
        const string uuid = "1234567890abcdef1234567890abcdef";
        Account acc = provider.Create("Steve", uuid);

        acc.Uuid.Should().Be(uuid, "a supplied bare UUID must be used verbatim (lowercased)");
        acc.Username.Should().Be("Steve");
    }

    [Fact]
    public void Custom_dashed_uuid_is_normalized_to_bare()
    {
        var provider = new OfflineAuthProvider();
        Account acc = provider.Create("Alex", "12345678-1234-1234-1234-1234567890AB");

        acc.Uuid.Should().Be("123456781234123412341234567890ab", "dashes are stripped + lowercased");
    }

    [Fact]
    public void Empty_uuid_falls_back_to_deterministic()
    {
        var provider = new OfflineAuthProvider();
        Account auto = provider.Create("Player");
        Account blank = provider.Create("Player", uuid: null);
        Account whitespace = provider.Create("Player", "   ");

        blank.Uuid.Should().Be(auto.Uuid, "null UUID falls back to the deterministic offline UUID");
        whitespace.Uuid.Should().Be(auto.Uuid, "whitespace UUID falls back too");
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 33 chars
    [InlineData("g1234567890abcdef1234567890abcdef")] // non-hex char
    public void Rejects_invalid_custom_uuid(string bad)
    {
        var provider = new OfflineAuthProvider();
        Action act = () => provider.Create("Player", bad);
        act.Should().Throw<ArgumentException>("an invalid UUID must be rejected, not silently mangled");
    }
}
