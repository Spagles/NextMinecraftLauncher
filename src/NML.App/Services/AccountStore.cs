using System.Text.Json;
using NML.AICore.Secrets;
using NML.Core.Auth;
using NML.Core.Auth.Microsoft;

namespace NML.App.Services;

/// <summary>
/// Persists the list of <see cref="Account"/>s and which one is currently active.
/// Access tokens for Microsoft/authlib-injector accounts are encrypted via
/// <see cref="ISecretStore"/> (DPAPI on Windows) — never written in plaintext.
/// </summary>
public sealed class AccountStore
{
    private readonly string _file;
    private readonly string _activeFile;
    private readonly ISecretStore? _secrets;

    public AccountStore(string settingsDir, ISecretStore? secrets = null)
    {
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, "accounts.json");
        _activeFile = Path.Combine(settingsDir, "active_account.txt");
        _secrets = secrets;
    }

    public List<Account> LoadAll()
    {
        if (!File.Exists(_file)) return new List<Account>();
        try
        {
            string json = File.ReadAllText(_file);
            var accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();

            // Decrypt access tokens (and refresh tokens) from the secret store when available; when
            // there's no store (or no key), clear any encrypted placeholders so the literal string
            // "***encrypted***" never leaks out as an actual token (e.g. accounts.json copied to a
            // machine without the DPAPI key).
            for (int i = 0; i < accounts.Count; i++)
            {
                Account acc = accounts[i];
                if (string.IsNullOrEmpty(acc.Uuid) || acc.AccountType == "legacy") continue;
                string? token = _secrets?.GetAsync($"account:{acc.Uuid}").GetAwaiter().GetResult();
                string? refresh = _secrets?.GetAsync($"account:{acc.Uuid}:refresh").GetAwaiter().GetResult();
                string accessToken = !string.IsNullOrEmpty(token)
                    ? token
                    : (acc.AccessToken == "***encrypted***" ? string.Empty : acc.AccessToken);
                string refreshToken = !string.IsNullOrEmpty(refresh)
                    ? refresh
                    : (acc.RefreshToken == "***encrypted***" ? string.Empty : acc.RefreshToken);
                accounts[i] = acc with { AccessToken = accessToken, RefreshToken = refreshToken };
            }
            return accounts;
        }
        catch { return new List<Account>(); }
    }

    public void Save(List<Account> accounts)
    {
        // Encrypt access tokens AND refresh tokens before writing to disk (if secret store available).
        List<Account> toSerialize = accounts;
        if (_secrets is not null)
        {
            toSerialize = new List<Account>();
            foreach (Account acc in accounts)
            {
                if (!string.IsNullOrEmpty(acc.Uuid) && acc.AccountType != "legacy")
                {
                    if (!string.IsNullOrEmpty(acc.AccessToken))
                        _secrets.SetAsync($"account:{acc.Uuid}", acc.AccessToken).GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(acc.RefreshToken))
                        _secrets.SetAsync($"account:{acc.Uuid}:refresh", acc.RefreshToken).GetAwaiter().GetResult();
                    // Write placeholders in the JSON (never the real tokens).
                    toSerialize.Add(acc with
                    {
                        AccessToken = "***encrypted***",
                        RefreshToken = "***encrypted***",
                    });
                }
                else
                {
                    toSerialize.Add(acc);
                }
            }
        }

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_file, JsonSerializer.Serialize(toSerialize, opts));
    }

    public string? GetActiveUuid() =>
        File.Exists(_activeFile) ? File.ReadAllText(_activeFile).Trim() : null;

    public void SetActiveUuid(string uuid) => File.WriteAllText(_activeFile, uuid);

    /// <summary>
    /// Silently refresh every Microsoft account whose access token is past (or near) expiry and
    /// has a stored refresh token. Returns the refreshed list. Accounts whose refresh fails (e.g.
    /// refresh token revoked) are left untouched so the UI can prompt a fresh device-code login.
    /// This is the multi-account "keep them all live" path.
    /// </summary>
    public async Task<List<Account>> RefreshIfDueAsync(MicrosoftAuthProvider microsoft, CancellationToken ct = default)
    {
        var accounts = LoadAll();
        bool changed = false;
        for (int i = 0; i < accounts.Count; i++)
        {
            Account acc = accounts[i];
            if (acc.AccountType != "msa" || !acc.NeedsRefresh || !acc.CanRefreshSilently) continue;
            try
            {
                Account refreshed = await microsoft.ReLoginAsync(acc.RefreshToken, ct).ConfigureAwait(false);
                // Preserve the original client id if the provider didn't set it.
                if (string.IsNullOrEmpty(refreshed.MsaClientId))
                    refreshed = refreshed with { MsaClientId = acc.MsaClientId };
                accounts[i] = refreshed;
                changed = true;
            }
            catch
            {
                // Refresh failed (revoked token, network) — leave the stale account so the UI
                // can prompt re-authentication rather than silently dropping it.
            }
        }
        if (changed) Save(accounts);
        return accounts;
    }
}
