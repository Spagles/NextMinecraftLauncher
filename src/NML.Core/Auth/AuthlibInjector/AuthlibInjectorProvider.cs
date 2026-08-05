using System.Text.Json;
using System.Text.Json.Serialization;
using NML.Core.Auth;

namespace NML.Core.Auth.AuthlibInjector;

/// <summary>
/// Configuration for an external Yggdrasil (authlib-injector) login server, like LittleSkin
/// or any Bakaxy/Yggdrasil-API-compatible skin station. This is HMCL's signature feature —
/// log in with a non-Microsoft account backed by a community server.
/// </summary>
public sealed class AuthlibInjectorServer
{
    /// <summary>Display name (e.g. "LittleSkin").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The API root URL (e.g. <c>https://littleskin.cn/api/yggdrasil</c>).</summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>The server's public-key metadata, fetched once and cached. The injector uses
    /// this to override Mojang's authlib at launch.</summary>
    public ServerMetadata? Metadata { get; set; }

    /// <summary>SHA-256 of the server's metadata JSON — used as a stable server identifier.</summary>
    public string? MetadataHash { get; set; }
}

/// <summary>The metadata document served by an authlib-injector server at its API root.</summary>
public sealed class ServerMetadata
{
    [JsonPropertyName("meta")]
    public ServerMeta? Meta { get; init; }

    [JsonPropertyName("signaturePublickey")]
    public string? PublicKey { get; init; }

    /// <summary>Skin domains that this server is allowed to serve textures for.</summary>
    [JsonPropertyName("skinDomains")]
    public List<string> SkinDomains { get; init; } = new();
}

public sealed class ServerMeta
{
    [JsonPropertyName("serverName")]
    public string? ServerName { get; init; }

    [JsonPropertyName("implementationName")]
    public string? ImplementationName { get; init; }

    [JsonPropertyName("implementationVersion")]
    public string? ImplementationVersion { get; init; }
}

/// <summary>
/// Provides authlib-injector login against an external Yggdrasil server. Implements the
/// classic <c>/authserver/authenticate</c> + <c>/authserver/refresh</c> endpoints so a
/// user can sign in with their skin-station account. The resulting access token + skin
/// server URL are passed to the launcher, which prepends the authlib-injector java agent.
/// </summary>
public sealed class AuthlibInjectorProvider
{
    private readonly Core.Download.IHttpFetcher _http;

    public AuthlibInjectorProvider(Core.Download.IHttpFetcher http) => _http = http;

    /// <summary>Fetch + cache the server's metadata document (used to validate + build the agent arg).</summary>
    public async Task<AuthlibInjectorServer> ResolveServerAsync(AuthlibInjectorServer server, CancellationToken ct = default)
    {
        if (server.Metadata is not null) return server;

        string metaJson = await _http.GetStringAsync(server.ApiUrl, ct);
        var meta = JsonSerializer.Deserialize<ServerMetadata>(metaJson) ?? new ServerMetadata();
        server.Metadata = meta;

        // Hash the raw JSON for a stable identifier.
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(metaJson));
        server.MetadataHash = System.Convert.ToHexString(hash).ToLowerInvariant();

        if (string.IsNullOrEmpty(server.Name) && meta.Meta?.ServerName is not null)
            server.Name = meta.Meta.ServerName;

        return server;
    }

    /// <summary>
    /// Authenticate against the Yggdrasil server's <c>/authserver/authenticate</c> endpoint.
    /// Returns an <see cref="Account"/> with the access token + the server URL for the launcher.
    /// </summary>
    public async Task<Account> LoginAsync(
        AuthlibInjectorServer server,
        string username,
        string password,
        CancellationToken ct = default)
    {
        string url = server.ApiUrl.TrimEnd('/') + "/authserver/authenticate";
        var payload = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username,
            password,
        };
        string body = JsonSerializer.Serialize(payload);
        using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");

        // The fetcher interface gives us bytes; we POST via a small HttpClient here.
        using var client = new System.Net.Http.HttpClient();
        using var resp = await client.PostAsync(url, content, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            using var err = JsonDocument.Parse(json);
            string errMsg = err.RootElement.TryGetProperty("errorMessage", out var e) ? e.GetString() ?? "error" : resp.StatusCode.ToString();
            throw new InvalidOperationException($"Yggdrasil login failed: {errMsg}");
        }

        using var doc = JsonDocument.Parse(json);
        string accessToken = doc.RootElement.GetProperty("accessToken").GetString() ?? string.Empty;
        var selected = doc.RootElement.GetProperty("selectedProfile");
        string id = selected.GetProperty("id").GetString() ?? string.Empty;
        string name = selected.GetProperty("name").GetString() ?? username;

        return new Account
        {
            Username = name,
            Uuid = id,
            AccessToken = accessToken,
            AccountType = "authlib-injector",
            // Stash the server URL in Xuid's slot so the launcher can build the agent arg.
            Xuid = server.ApiUrl,
        };
    }

    /// <summary>The authlib-injector java agent JAR download URL (the official release on BMCLAPI/maven).</summary>
    public const string AuthlibInjectorJarUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
}
