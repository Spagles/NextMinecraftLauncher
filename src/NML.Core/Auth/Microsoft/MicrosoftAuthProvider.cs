using Microsoft.Extensions.Logging;

namespace NML.Core.Auth.Microsoft;

/// <summary>
/// Orchestrates the full Microsoft → Xbox Live → XSTS → Minecraft device-code login flow.
/// Exposes a callback-based API so the UI can show the user the verification URL/code and
/// poll until sign-in completes (or the flow expires).
/// </summary>
public sealed class MicrosoftAuthProvider : IAuthProvider
{
    /// <summary>
    /// The fixed Azure app client_id used by the official Minecraft launcher. All third-party
    /// launchers reuse this because Microsoft has not opened a separate one for the community.
    /// </summary>
    public const string ClientId = "00000000402b5328";

    /// <summary>The OAuth scope for the legacy MSA flow (pairs with login.live.com endpoints).</summary>
    public const string Scope = "service::user.auth.xboxlive.com::MBI_SSL";

    /// <summary>The legacy authorize URL (browser-based flow — the only working approach for this client_id).</summary>
    public const string AuthorizeUrl = "https://login.live.com/oauth20_authorize.srf";
    public const string TokenExchangeUrl = "https://login.live.com/oauth20_token.srf";
    public const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";

    private readonly IMicrosoftExchange _exchange;
    private readonly ILogger<MicrosoftAuthProvider> _logger;

    public MicrosoftAuthProvider(IMicrosoftExchange exchange, ILogger<MicrosoftAuthProvider> logger)
    {
        _exchange = exchange;
        _logger = logger;
    }

    public string Type => "msa";

    /// <summary>
    /// Build the browser authorization URL for the legacy MSA flow. The caller opens this in the
    /// system browser; after sign-in the browser redirects to the redirect_uri with ?code=XXX.
    /// </summary>
    public string GetAuthorizeUrl()
    {
        return $"{AuthorizeUrl}?client_id={ClientId}&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
               $"&scope={Uri.EscapeDataString(Scope)}" +
               $"&prompt=login";
    }

    /// <summary>
    /// Complete the login flow: exchange the auth code (from the browser redirect) for an MSA
    /// token, then proceed through XBL → XSTS → Minecraft → profile. Returns the final Account.
    /// </summary>
    public async Task<Account> CompleteLoginWithCodeAsync(string authCode, CancellationToken ct = default)
    {
        _logger.LogInformation("Exchanging auth code for MSA token…");
        MsaTokenResponse msa = await _exchange.ExchangeAuthCodeForMsaTokenAsync(
            ClientId, authCode, RedirectUri, Scope, ct);

        _logger.LogInformation("MSA token obtained, exchanging → Xbox Live…");
        XblTokenResponse xbl = await _exchange.ExchangeMsaForXblAsync(msa.AccessToken, ct);

        _logger.LogDebug("Exchanging XBL → XSTS token…");
        XstsTokenResponse xsts = await _exchange.ExchangeXblForXstsAsync(xbl.Token, ct);

        string userHash = xsts.DisplayClaims?.Xui.FirstOrDefault()?.UserHash
                          ?? throw new InvalidOperationException("XSTS response missing user hash (uhs).");

        _logger.LogDebug("Exchanging XSTS → Minecraft token…");
        MinecraftTokenResponse mc = await _exchange.ExchangeXstsForMinecraftAsync(xsts.Token, userHash, ct);

        _logger.LogDebug("Fetching Minecraft profile…");
        MinecraftProfile profile = await _exchange.GetMinecraftProfileAsync(mc.AccessToken, ct);

        _logger.LogInformation("Logged in as {Name}.", profile.Name);
        return new Account
        {
            Username = profile.Name,
            Uuid = profile.Id,
            AccessToken = mc.AccessToken,
            AccountType = "msa",
            Xuid = userHash, // uhs is the closest stable id; full XUID would need a separate call.
            // Capture the refresh token + expiry so the launcher can silently re-login later
            // (multi-account workflows: several MSA accounts kept live without re-doing device-code).
            RefreshToken = msa.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, msa.ExpiresIn)),
            MsaClientId = ClientId,
        };
    }

    /// <summary>Silent re-login using a stored MSA refresh token. Returns a refreshed Account
    /// carrying a fresh access token, a rotated refresh token, and a new expiry.</summary>
    public async Task<Account> ReLoginAsync(string refreshToken, CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing Microsoft session…");
        MsaTokenResponse msa = await _exchange.RefreshMsaTokenAsync(ClientId, refreshToken, Scope, ct);
        XblTokenResponse xbl = await _exchange.ExchangeMsaForXblAsync(msa.AccessToken, ct);
        XstsTokenResponse xsts = await _exchange.ExchangeXblForXstsAsync(xbl.Token, ct);
        string userHash = xsts.DisplayClaims?.Xui.FirstOrDefault()?.UserHash
                          ?? throw new InvalidOperationException("XSTS response missing user hash.");
        MinecraftTokenResponse mc = await _exchange.ExchangeXstsForMinecraftAsync(xsts.Token, userHash, ct);
        MinecraftProfile profile = await _exchange.GetMinecraftProfileAsync(mc.AccessToken, ct);

        return new Account
        {
            Username = profile.Name,
            Uuid = profile.Id,
            AccessToken = mc.AccessToken,
            AccountType = "msa",
            Xuid = userHash,
            // MS refresh tokens rotate: persist the new one so future refreshes keep working.
            RefreshToken = msa.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, msa.ExpiresIn)),
            MsaClientId = ClientId,
        };
    }
}
