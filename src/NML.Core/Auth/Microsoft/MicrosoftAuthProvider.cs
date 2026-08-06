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

    /// <summary>The OAuth scope that grants the MSA token access to Xbox Live.</summary>
    public const string Scope = "service::user.auth.xboxlive.com::MBI_SSL";

    private readonly IMicrosoftExchange _exchange;
    private readonly ILogger<MicrosoftAuthProvider> _logger;

    public MicrosoftAuthProvider(IMicrosoftExchange exchange, ILogger<MicrosoftAuthProvider> logger)
    {
        _exchange = exchange;
        _logger = logger;
    }

    public string Type => "msa";

    /// <summary>
    /// Begin the device-code flow. Returns the <see cref="DeviceCodeResponse"/> to display;
    /// the caller then invokes <see cref="PollForCompletionAsync"/> to await the user.
    /// </summary>
    public async Task<DeviceCodeResponse> BeginLoginAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting Microsoft device code…");
        return await _exchange.RequestDeviceCodeAsync(ClientId, Scope, ct);
    }

    /// <summary>
    /// Poll the device-code endpoint until the user completes sign-in (or the flow expires).
    /// <paramref name="onProgress"/> is invoked on every poll iteration (for UI spinners).
    /// </summary>
    public async Task<Account> PollForCompletionAsync(
        DeviceCodeResponse deviceCode,
        Func<DeviceCodeResponse, bool>? onProgress = null,
        CancellationToken ct = default)
    {
        int intervalMs = Math.Max(2, deviceCode.Interval) * 1000;
        DateTime deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresIn).UtcDateTime;

        MsaTokenResponse msa;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            onProgress?.Invoke(deviceCode);

            if (DateTimeOffset.UtcNow.UtcDateTime >= deadline)
                throw new TimeoutException("Microsoft device-code login expired.");

            try
            {
                msa = await _exchange.PollForMsaTokenAsync(ClientId, deviceCode.DeviceCode, Scope, ct);
                _logger.LogInformation("Microsoft account sign-in confirmed.");
                break;
            }
            catch (AuthorizationPendingException)
            {
                await Task.Delay(intervalMs, ct);
            }
        }

        // MSA → Xbox Live → XSTS → Minecraft → profile.
        _logger.LogDebug("Exchanging MSA → Xbox Live token…");
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
