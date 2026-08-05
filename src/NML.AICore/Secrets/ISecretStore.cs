namespace NML.AICore.Secrets;

/// <summary>
/// Stores secrets (API keys) at rest, encrypted. On Windows the implementation uses
/// DPAPI (CurrentUser scope) so the key never leaves the user's profile; on other
/// platforms it falls back to a file with restrictive permissions.
/// </summary>
public interface ISecretStore
{
    /// <summary>Store a secret under <paramref name="key"/> (encrypts at rest).</summary>
    Task SetAsync(string key, string secret, CancellationToken ct = default);

    /// <summary>Read a stored secret, or null if absent.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Delete a stored secret.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
