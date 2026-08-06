using NML.AICore.Secrets;
using NML.App.Services;
using NML.Core.Auth;

namespace NML.App.Tests;

/// <summary>
/// Verifies the multi-account token-lifecycle logic behind silent refresh: the <see cref="Account"/>
/// record's NeedsRefresh/CanRefreshSilently derivation, and <see cref="AccountStore"/>'s encrypted
/// round-trip of the MSA refresh token (so several Microsoft accounts can be kept live across
/// restarts without re-doing the device-code flow).
/// </summary>
public class MultiAccountTokenRefreshTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-acct-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private class FakeSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _kv = new();
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_kv.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value, CancellationToken ct = default)
        { _kv[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default)
        { _kv.Remove(key); return Task.CompletedTask; }
    }

    [Fact]
    public void NeedsRefresh_True_Within_5_Minute_Safety_Margin()
    {
        // A token expiring in 3 minutes is treated as already due (5-minute proactive margin),
        // so a token that would lapse mid-session is renewed beforehand.
        var acc = new Account
        {
            Username = "p", Uuid = "u", AccountType = "msa",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3),
            RefreshToken = "rt", MsaClientId = "cid",
        };
        acc.NeedsRefresh.Should().BeTrue();
    }

    [Fact]
    public void NeedsRefresh_False_When_Token_Far_From_Expiry()
    {
        var acc = new Account
        {
            Username = "p", Uuid = "u", AccountType = "msa",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(20),
            RefreshToken = "rt", MsaClientId = "cid",
        };
        acc.NeedsRefresh.Should().BeFalse();
    }

    [Fact]
    public void NeedsRefresh_False_For_Offline_Accounts_And_When_No_Expiry()
    {
        // Offline accounts never expire.
        new Account { Username = "p", AccountType = "legacy" }.NeedsRefresh.Should().BeFalse();
        // MSA account with no recorded expiry (e.g. migrated data) doesn't claim to need refresh.
        new Account { Username = "p", AccountType = "msa" }.NeedsRefresh.Should().BeFalse();
    }

    [Fact]
    public void CanRefreshSilently_Requires_Refresh_Token_And_ClientId()
    {
        // Both refresh token + client id present → can refresh silently.
        new Account { AccountType = "msa", RefreshToken = "rt", MsaClientId = "cid" }
            .CanRefreshSilently.Should().BeTrue();
        // Missing either → cannot.
        new Account { AccountType = "msa", RefreshToken = "rt", MsaClientId = "" }
            .CanRefreshSilently.Should().BeFalse();
        new Account { AccountType = "msa", RefreshToken = "", MsaClientId = "cid" }
            .CanRefreshSilently.Should().BeFalse();
        // Offline → never.
        new Account { AccountType = "legacy", RefreshToken = "rt", MsaClientId = "cid" }
            .CanRefreshSilently.Should().BeFalse();
    }

    [Fact]
    public void AccountStore_RoundTrips_Refresh_Token_Encrypted()
    {
        // The refresh token must survive a save→load cycle through the secret store, so a stored
        // MSA account stays refreshable after a restart. Previously the refresh token was dropped
        // at login and never persisted.
        string dir = TempDir();
        try
        {
            var secrets = new FakeSecretStore();
            var store = new AccountStore(dir, secrets);
            var account = new Account
            {
                Username = "Steve", Uuid = "uuid1", AccountType = "msa",
                AccessToken = "at-secret", RefreshToken = "rt-secret",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), MsaClientId = "cid",
            };
            store.Save(new List<Account> { account });

            // On disk the JSON must carry placeholders, never the real tokens.
            string json = File.ReadAllText(Path.Combine(dir, "accounts.json"));
            json.Should().NotContain("at-secret");
            json.Should().NotContain("rt-secret");
            json.Should().Contain("***encrypted***");

            // Loading back decrypts both tokens.
            var loaded = store.LoadAll().Single();
            loaded.AccessToken.Should().Be("at-secret");
            loaded.RefreshToken.Should().Be("rt-secret");
            loaded.ExpiresAt.Should().BeCloseTo(account.ExpiresAt!.Value, TimeSpan.FromSeconds(1));
            loaded.MsaClientId.Should().Be("cid");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AccountStore_Rehydrates_Refresh_Token_On_New_Machine()
    {
        // If the secret store lacks the key (e.g. accounts.json copied to a new machine), the
        // encrypted placeholder must be cleared (not leaked as the literal "***encrypted***").
        string dir = TempDir();
        try
        {
            // Save with secrets present, then load with an EMPTY secret store.
            var store = new AccountStore(dir, new FakeSecretStore());
            store.Save(new List<Account>
            {
                new() { Username = "S", Uuid = "u", AccountType = "msa",
                        AccessToken = "at", RefreshToken = "rt",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), MsaClientId = "cid" },
            });

            var freshStore = new AccountStore(dir, secrets: null); // no secret store → no decryption
            var loaded = freshStore.LoadAll().Single();
            loaded.AccessToken.Should().BeEmpty("placeholder cleared on a machine without the key");
            loaded.RefreshToken.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
