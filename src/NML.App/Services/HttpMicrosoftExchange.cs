using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NML.Core.Auth.Microsoft;

namespace NML.App.Services;

/// <summary>
/// Concrete <see cref="IMicrosoftExchange"/> that performs the real HTTP calls of the
/// Microsoft device-code login flow. The endpoints are the long-stable ones used by every
/// community launcher (the official Minecraft launcher's fixed client_id).
/// </summary>
public sealed class HttpMicrosoftExchange : IMicrosoftExchange
{
    private const string DeviceCodeUrl = "https://login.live.com/oauth20_devicecode.srf";
    private const string TokenUrl = "https://login.live.com/oauth20_token.srf";
    private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string MinecraftLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string MinecraftProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    private readonly HttpClient _http;

    public HttpMicrosoftExchange(HttpClient http) => _http = http;

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId, string scope, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = scope,
        });
        using var resp = await _http.PostAsync(DeviceCodeUrl, body, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DeviceCodeResponse>(ct)
               ?? throw new InvalidDataException("Device-code response was null.");
    }

    public async Task<MsaTokenResponse> PollForMsaTokenAsync(string clientId, string deviceCode, string scope, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["scope"] = scope,
        });
        using var resp = await _http.PostAsync(TokenUrl, body, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);

        // 400 with "authorization_pending" is the expected poll-again signal.
        if (!resp.IsSuccessStatusCode)
        {
            using var err = JsonDocument.Parse(json);
            string error = err.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            if (error == "authorization_pending")
                throw new AuthorizationPendingException();
            if (error == "expired_token")
                throw new TimeoutException("Microsoft device-code login expired.");
            if (error == "slow_down")
                throw new AuthorizationPendingException();
            throw new InvalidOperationException($"MSA token error: {error}");
        }

        return JsonSerializer.Deserialize<MsaTokenResponse>(json)
               ?? throw new InvalidDataException("MSA token response was null.");
    }

    public async Task<MsaTokenResponse> RefreshMsaTokenAsync(string clientId, string refreshToken, string scope, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = scope,
        });
        using var resp = await _http.PostAsync(TokenUrl, body, ct);
        resp.EnsureSuccessStatusCode();
        string json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<MsaTokenResponse>(json)
               ?? throw new InvalidDataException("MSA refresh response was null.");
    }

    public async Task<MsaTokenResponse> ExchangeAuthCodeForMsaTokenAsync(
        string clientId, string authCode, string redirectUri, string scope, CancellationToken ct = default)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = authCode,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
        });
        using var resp = await _http.PostAsync(TokenUrl, body, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"MSA token exchange failed: {json}");
        return JsonSerializer.Deserialize<MsaTokenResponse>(json)
               ?? throw new InvalidDataException("MSA auth-code exchange response was null.");
    }

    public async Task<XblTokenResponse> ExchangeMsaForXblAsync(string msaAccessToken, CancellationToken ct = default)
    {
        var body = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msaAccessToken,
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        };
        using var resp = await PostJsonAsync(XblUrl, body, ct);
        return await resp.Content.ReadFromJsonAsync<XblTokenResponse>(ct)
               ?? throw new InvalidDataException("XBL token response was null.");
    }

    public async Task<XstsTokenResponse> ExchangeXblForXstsAsync(string xblToken, CancellationToken ct = default)
    {
        var body = new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        };
        using var resp = await PostJsonAsync(XstsUrl, body, ct);
        return await resp.Content.ReadFromJsonAsync<XstsTokenResponse>(ct)
               ?? throw new InvalidDataException("XSTS token response was null.");
    }

    public async Task<MinecraftTokenResponse> ExchangeXstsForMinecraftAsync(string xstsToken, string userHash, CancellationToken ct = default)
    {
        var body = new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" };
        using var resp = await PostJsonAsync(MinecraftLoginUrl, body, ct);
        return await resp.Content.ReadFromJsonAsync<MinecraftTokenResponse>(ct)
               ?? throw new InvalidDataException("Minecraft token response was null.");
    }

    public async Task<MinecraftProfile> GetMinecraftProfileAsync(string mcAccessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccessToken);
        using var resp = await _http.SendAsync(req, ct);
        // 404/204 means the account doesn't own Minecraft — return an empty profile so the
        // caller can show a clear "you don't own Minecraft" message.
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound ||
            resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            return new MinecraftProfile { Id = string.Empty, Name = string.Empty };
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MinecraftProfile>(ct)
               ?? throw new InvalidDataException("Minecraft profile response was null.");
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // The Xbox Live + XSTS endpoints require this header or they return 400.
        if (url.Contains("xboxlive.com", StringComparison.OrdinalIgnoreCase))
            req.Headers.Add("x-xbl-contract-version", "1");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return resp;
    }
}
