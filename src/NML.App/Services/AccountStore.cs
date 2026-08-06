using System.Text.Json;
using NML.AICore.Secrets;
using NML.Core.Auth;

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

            // Decrypt access tokens from the secret store (if available).
            if (_secrets is not null)
            {
                for (int i = 0; i < accounts.Count; i++)
                {
                    Account acc = accounts[i];
                    if (!string.IsNullOrEmpty(acc.Uuid) && acc.AccountType != "legacy")
                    {
                        string? token = _secrets.GetAsync($"account:{acc.Uuid}").GetAwaiter().GetResult();
                        if (!string.IsNullOrEmpty(token))
                            accounts[i] = acc with { AccessToken = token };
                        else if (acc.AccessToken == "***encrypted***")
                            // Secret store doesn't have the key (e.g. new machine) — clear
                            // the placeholder so the app knows to re-authenticate.
                            accounts[i] = acc with { AccessToken = string.Empty };
                    }
                }
            }
            return accounts;
        }
        catch { return new List<Account>(); }
    }

    public void Save(List<Account> accounts)
    {
        // Encrypt access tokens before writing to disk (if secret store available).
        List<Account> toSerialize = accounts;
        if (_secrets is not null)
        {
            toSerialize = new List<Account>();
            foreach (Account acc in accounts)
            {
                if (!string.IsNullOrEmpty(acc.Uuid) && acc.AccountType != "legacy" &&
                    !string.IsNullOrEmpty(acc.AccessToken))
                {
                    // Store the token encrypted.
                    _secrets.SetAsync($"account:{acc.Uuid}", acc.AccessToken).GetAwaiter().GetResult();
                    // Write a placeholder in the JSON (never the real token).
                    toSerialize.Add(acc with { AccessToken = "***encrypted***" });
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
}
