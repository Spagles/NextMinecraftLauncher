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
}
