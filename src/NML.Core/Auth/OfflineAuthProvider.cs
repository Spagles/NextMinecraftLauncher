using System.Security.Cryptography;
using System.Text;

namespace NML.Core.Auth;

/// <summary>
/// Represents an authenticated Minecraft account, independent of which provider
/// produced it. This is the value object the launcher stores and uses at launch time.
/// </summary>
public sealed class Account
{
    public string Username { get; init; } = string.Empty;

    /// <summary>Mojang-format UUID without dashes, generated deterministically for offline.</summary>
    public string Uuid { get; init; } = string.Empty;

    public string AccessToken { get; init; } = string.Empty;

    /// <summary><c>legacy</c> (offline) or <c>msa</c> (Microsoft online).</summary>
    public string AccountType { get; init; } = "legacy";

    /// <summary>Xbox Live XUID (Microsoft accounts only; empty for offline).</summary>
    public string Xuid { get; init; } = string.Empty;

    /// <summary>Display type for the UI.</summary>
    public bool IsOffline => AccountType == "legacy";
}

public interface IAuthProvider
{
    string Type { get; }
}

public interface IOfflineAuthProvider : IAuthProvider
{
    /// <summary>Create an offline account from a username. UUID is deterministic (offline-mode v3).</summary>
    Account Create(string username);
}

/// <summary>
/// Offline-mode account generator. UUID is the standard "offline" v3 UUID derived
/// from the username via MD5 (matches what vanilla servers/clients expect for offline).
/// Access token is a random opaque string — never validated offline.
/// </summary>
public sealed class OfflineAuthProvider : IOfflineAuthProvider
{
    public string Type => "legacy";

    public Account Create(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));

        // Offline UUID: MD5("OfflinePlayer:" + username), formatted as v3 UUID without dashes.
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        // Set version (3) and variant bits per RFC 4122.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        string uuid = Convert.ToHexString(hash).ToLowerInvariant();

        // Random access token (opaque, only validated in online mode).
        Span<byte> tokenBytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(tokenBytes);
        string accessToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        return new Account
        {
            Username = username,
            Uuid = uuid,
            AccessToken = accessToken,
            AccountType = "legacy",
        };
    }
}
