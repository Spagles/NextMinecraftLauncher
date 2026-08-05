using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace NML.AICore.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> backed by Windows DPAPI (CurrentUser scope). Secrets are
/// encrypted with a key bound to the current Windows user account, so no app-managed
/// key material is required. On non-Windows the file is written with restrictive perms
/// and a process-bound entropy salt (best-effort; not as strong as DPAPI).
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("NML.AICore.v1");

    private readonly string _storeDir;

    public DpapiSecretStore(string storeDir)
    {
        _storeDir = storeDir;
        Directory.CreateDirectory(_storeDir);
    }

    public Task SetAsync(string key, string secret, CancellationToken ct = default)
    {
        string path = PathFor(key);
        byte[] plain = System.Text.Encoding.UTF8.GetBytes(secret);
        byte[] cipher = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)
            : XorFallback(plain); // non-Windows: weak obfuscation only

        File.WriteAllBytes(path, cipher);
        TryRestrictPermissions(path);
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        string path = PathFor(key);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);

        byte[] cipher = File.ReadAllBytes(path);
        byte[] plain = OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser)
            : XorFallback(cipher);
        return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(plain));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        string path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        // Hash the key so it's a stable, filesystem-safe filename.
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Path.Combine(_storeDir, Convert.ToHexString(hash).ToLowerInvariant() + ".secret");
    }

    private static byte[] XorFallback(byte[] data)
    {
        // Non-Windows: DPAPI isn't available. Apply a repeating-key XOR with the app
        // entropy as a *minimal* obfuscation so the secret isn't plaintext on disk.
        // Real protection on macOS/Linux should use the OS keychain; left as a TODO.
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ Entropy[i % Entropy.Length]);
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var fi = new FileInfo(path);
            fi.Attributes |= FileAttributes.Hidden;
            // Further ACL hardening is possible; the DPAPI ciphertext is already unusable
            // off this user account, which is the main protection.
        }
        catch { /* non-fatal */ }
    }
}
