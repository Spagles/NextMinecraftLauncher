namespace NML.Core.Auth.Microsoft;

/// <summary>
/// Injected HTTP exchange layer for the Microsoft auth flow. Kept as an interface so
/// the multi-step flow can be unit-tested with canned responses instead of real network
/// calls. The production implementation wraps <c>HttpClient</c> with JSON POSTs.
/// </summary>
public interface IMicrosoftExchange
{
    /// <summary>Step 1: request a device code (and the URL the user must visit).</summary>
    Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId, string scope, CancellationToken ct = default);

    /// <summary>Step 2: poll for the MSA token. Throws <see cref="AuthorizationPendingException"/> if not done yet.</summary>
    Task<MsaTokenResponse> PollForMsaTokenAsync(
        string clientId, string deviceCode, string scope, CancellationToken ct = default);

    /// <summary>Step 2b: silent re-login using a previously stored refresh token.</summary>
    Task<MsaTokenResponse> RefreshMsaTokenAsync(
        string clientId, string refreshToken, string scope, CancellationToken ct = default);

    /// <summary>Step 2c: exchange an authorization code (from browser redirect) for an MSA token.</summary>
    Task<MsaTokenResponse> ExchangeAuthCodeForMsaTokenAsync(
        string clientId, string authCode, string redirectUri, string scope, CancellationToken ct = default);

    /// <summary>Step 3: MSA token → Xbox Live user token.</summary>
    Task<XblTokenResponse> ExchangeMsaForXblAsync(string msaAccessToken, CancellationToken ct = default);

    /// <summary>Step 4: XBL token → XSTS token.</summary>
    Task<XstsTokenResponse> ExchangeXblForXstsAsync(string xblToken, CancellationToken ct = default);

    /// <summary>Step 5: XSTS token + user hash → Minecraft access token.</summary>
    Task<MinecraftTokenResponse> ExchangeXstsForMinecraftAsync(
        string xstsToken, string userHash, CancellationToken ct = default);

    /// <summary>Step 6: fetch the Minecraft profile (UUID/username).</summary>
    Task<MinecraftProfile> GetMinecraftProfileAsync(string mcAccessToken, CancellationToken ct = default);
}

/// <summary>Thrown when polling the device-code endpoint before the user has completed sign-in.</summary>
public sealed class AuthorizationPendingException : Exception
{
    public AuthorizationPendingException() : base("Authorization pending.") { }
    public AuthorizationPendingException(string message) : base(message) { }
}
