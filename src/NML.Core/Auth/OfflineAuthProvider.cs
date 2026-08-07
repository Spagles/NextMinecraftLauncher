using System.Security.Cryptography;
using System.Text;

namespace NML.Core.Auth;

/// <summary>
/// Represents an authenticated Minecraft account, independent of which provider
/// produced it. This is the value object the launcher stores and uses at launch time.
/// </summary>
public sealed record Account
{
    public string Username { get; init; } = string.Empty;

    /// <summary>Mojang-format UUID without dashes, generated deterministically for offline.</summary>
    public string Uuid { get; init; } = string.Empty;

    public string AccessToken { get; init; } = string.Empty;

    /// <summary><c>legacy</c> (offline) or <c>msa</c> (Microsoft online).</summary>
    public string AccountType { get; init; } = "legacy";

    /// <summary>Xbox Live XUID (Microsoft accounts only; empty for offline).</summary>
    public string Xuid { get; init; } = string.Empty;

    /// <summary>MSA refresh token (Microsoft accounts only). Lets the launcher silently re-login
    /// instead of replaying the device-code flow on every token expiry. Persisted encrypted.</summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>UTC instant the <see cref="AccessToken"/> expires (Microsoft accounts only).
    /// Offline/legacy accounts leave this at the default (never expires).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The OAuth client id used to obtain this token (so the refresh path knows which
    /// client to call). Microsoft accounts only; empty for offline.</summary>
    public string MsaClientId { get; init; } = string.Empty;

    /// <summary>Display type for the UI.</summary>
    public bool IsOffline => AccountType == "legacy";

    /// <summary>
    /// True when this account's access token is past (or near) expiry and should be refreshed
    /// before launch. Offline accounts never need a refresh. A 5-minute safety margin is applied
    /// so a token expiring mid-session is renewed proactively. Pure + testable.
    /// </summary>
    public bool NeedsRefresh =>
        !IsOffline
        && ExpiresAt.HasValue
        && DateTimeOffset.UtcNow > ExpiresAt.Value.AddMinutes(-5);

    /// <summary>True when the account has the data required to attempt a silent refresh
    /// (a refresh token + the client id that issued it).</summary>
    public bool CanRefreshSilently => !IsOffline
        && !string.IsNullOrWhiteSpace(RefreshToken)
        && !string.IsNullOrWhiteSpace(MsaClientId);
}

public interface IAuthProvider
{
    string Type { get; }
}

public interface IOfflineAuthProvider : IAuthProvider
{
    /// <summary>Create an offline account from a username. UUID is deterministic (offline-mode v3).</summary>
    Account Create(string username);

    /// <summary>Create an offline account from a username + an optional custom UUID. When
    /// <paramref name="uuid"/> is null/empty, the deterministic offline v3 UUID is used; otherwise
    /// the supplied UUID (dashes stripped) is honored — useful for matching server-side player data
    /// or keeping a fixed identity across reinstalls.</summary>
    Account Create(string username, string? uuid);
}

/// <summary>
/// Offline-mode account generator. UUID is the standard "offline" v3 UUID derived
/// from the username via MD5 (matches what vanilla servers/clients expect for offline).
/// Access token is a random opaque string — never validated offline.
/// </summary>
public sealed class OfflineAuthProvider : IOfflineAuthProvider
{
    public string Type => "legacy";

    public Account Create(string username) => Create(username, uuid: null);

    public Account Create(string username, string? uuid)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));

        string finalUuid;
        string? custom = uuid?.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            // Accept dashed (8-4-4-4-12) or bare 32-hex UUIDs; normalize to bare lowercase.
            string bare = custom.Replace("-", "").Trim();
            finalUuid = IsValidHexUuid(bare) ? bare.ToLowerInvariant() : throw new ArgumentException(
                $"Custom UUID '{custom}' is not a valid 32-char hex UUID.", nameof(uuid));
        }
        else
        {
            // Offline UUID: MD5("OfflinePlayer:" + username), formatted as v3 UUID without dashes.
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
            // Set version (3) and variant bits per RFC 4122.
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            finalUuid = Convert.ToHexString(hash).ToLowerInvariant();
        }

        // Random access token (opaque, only validated in online mode).
        Span<byte> tokenBytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(tokenBytes);
        string accessToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        return new Account
        {
            Username = username,
            Uuid = finalUuid,
            AccessToken = accessToken,
            AccountType = "legacy",
        };
    }

    /// <summary>True when <paramref name="s"/> is exactly 32 hex characters (a bare Mojang UUID).</summary>
    private static bool IsValidHexUuid(string s)
    {
        if (s.Length != 32) return false;
        foreach (char c in s)
            if (!IsHex(c)) return false;
        return true;
    }

    private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
