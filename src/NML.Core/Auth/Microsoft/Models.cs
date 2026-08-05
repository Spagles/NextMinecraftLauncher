using System.Text.Json.Serialization;

namespace NML.Core.Auth.Microsoft;

/// <summary>
/// Step 1 of the device-code flow: the response from the device-authorization endpoint,
/// telling the user where to go and what code to enter. Polled until the user completes it.
/// </summary>
public sealed class DeviceCodeResponse
{
    /// <summary>The short code the user enters at <see cref="VerificationUri"/>.</summary>
    [JsonPropertyName("user_code")]
    public string UserCode { get; init; } = string.Empty;

    /// <summary>The URL the user should visit (without the code appended).</summary>
    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; init; } = string.Empty;

    /// <summary>If present, this URL has the code pre-filled and can be opened directly.</summary>
    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; init; }

    /// <summary>The URL the launcher polls to check if the user finished.</summary>
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; init; } = string.Empty;

    /// <summary>Seconds between polls.</summary>
    [JsonPropertyName("interval")]
    public int Interval { get; init; } = 5;

    /// <summary>Whole-flow expiry in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; } = 900;

    /// <summary>Human-readable message the UI can show directly.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Step 2: the MSA access token returned after the user completes the device-code sign-in.
/// Carries <c>access_token</c> and the <c>refresh_token</c> for silent re-login later.
/// </summary>
public sealed class MsaTokenResponse
{
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "Bearer";
    [JsonPropertyName("scope")] public string Scope { get; init; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>Step 3: Xbox Live user token (from MSA token exchange).</summary>
public sealed class XblTokenResponse
{
    [JsonPropertyName("Token")] public string Token { get; init; } = string.Empty;
    [JsonPropertyName("DisplayClaims")] public XblDisplayClaims? DisplayClaims { get; init; }
}

public sealed class XblDisplayClaims
{
    [JsonPropertyName("xui")] public List<XblUserClaim> Xui { get; init; } = new();
}

public sealed class XblUserClaim
{
    [JsonPropertyName("uhs")] public string UserHash { get; init; } = string.Empty;
}

/// <summary>Step 4: Xbox Secure Token Service (XSTS) token + the user hash needed for MC login.</summary>
public sealed class XstsTokenResponse
{
    [JsonPropertyName("Token")] public string Token { get; init; } = string.Empty;
    [JsonPropertyName("DisplayClaims")] public XblDisplayClaims? DisplayClaims { get; init; }
}

/// <summary>Step 5: the Minecraft access token (from XSTS exchange).</summary>
public sealed class MinecraftTokenResponse
{
    [JsonPropertyName("username")] public string Username { get; init; } = string.Empty;
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "Bearer";
}

/// <summary>Step 6: the Minecraft profile (UUID, username, skin). The final identity.</summary>
public sealed class MinecraftProfile
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}
