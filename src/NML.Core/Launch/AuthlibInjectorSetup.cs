using System.Text.Json;
using Microsoft.Extensions.Logging;
using NML.Core.Auth.AuthlibInjector;
using NML.Core.Download;

namespace NML.Core.Launch;

/// <summary>
/// Manages the local authlib-injector java agent JAR. Fetches the latest artifact manifest from
/// the official authlib-injector.yushi.moe endpoint, downloads the JAR into the launcher's
/// cache, and returns the absolute path + the full <c>-javaagent:</c> argument to prepend.
/// </summary>
public sealed class AuthlibInjectorSetup
{
    /// <summary>The manifest URL listing the latest authlib-injector build.</summary>
    public const string LatestManifestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";

    private readonly IHttpFetcher _http;
    private readonly ILogger<AuthlibInjectorSetup> _logger;
    private readonly string _cacheDir;

    public AuthlibInjectorSetup(IHttpFetcher http, string cacheDir, ILogger<AuthlibInjectorSetup> logger)
    {
        _http = http;
        _cacheDir = cacheDir;
        _logger = logger;
    }

    /// <summary>
    /// Ensure the authlib-injector agent JAR is on disk and return the absolute path.
    /// Caches by SHA-256 so re-launches don't re-download.
    /// </summary>
    public async Task<string> EnsureAgentJarAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cacheDir);

        // Fetch the latest-build manifest: { "version": "...", "download_url": "...", "sha256": "..." }.
        string manifestJson = await _http.GetStringAsync(LatestManifestUrl, ct);
        using var doc = JsonDocument.Parse(manifestJson);

        string version = doc.RootElement.GetProperty("version").GetString() ?? "unknown";
        string downloadUrl = doc.RootElement.GetProperty("download_url").GetString() ?? string.Empty;
        string expectedSha256 = doc.RootElement.TryGetProperty("sha256", out var sh) ? sh.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(downloadUrl))
            throw new InvalidOperationException("authlib-injector manifest missing download_url.");

        string jarPath = Path.Combine(_cacheDir, $"authlib-injector-{version}.jar");

        // Idempotency: skip if a JAR for this exact version (and matching hash) is already present.
        if (File.Exists(jarPath) && await VerifySha256Async(jarPath, expectedSha256, ct))
        {
            _logger.LogDebug("authlib-injector {Version} already cached at {Path}.", version, jarPath);
            return jarPath;
        }

        _logger.LogInformation("Downloading authlib-injector {Version}…", version);
        byte[] bytes = await _http.GetByteArrayAsync(downloadUrl, ct);
        await File.WriteAllBytesAsync(jarPath, bytes, ct);
        return jarPath;
    }

    /// <summary>
    /// Build the full <c>-javaagent:jar=path/to/jar.jar=...</c> argument for the given server.
    /// authlib-injector accepts the server API URL as the agent argument; at runtime it fetches
    /// the server's metadata and patches Mojang's authlib classes so the game talks to the
    /// external Yggdrasil server.
    /// </summary>
    public static string BuildAgentArgument(string jarPath, AuthlibInjectorServer server) =>
        $"-javaagent:{jarPath}={server.ApiUrl}";

    private static async Task<bool> VerifySha256Async(string path, string expected, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(expected)) return true; // can't verify — trust
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(fs, ct);
        string actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
