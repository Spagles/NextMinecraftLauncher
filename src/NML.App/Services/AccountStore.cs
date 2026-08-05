using System.Text.Json;
using NML.Core.Auth;

namespace NML.App.Services;

/// <summary>
/// Persists the list of <see cref="Account"/>s and which one is currently active, as a JSON
/// file in the launcher's settings directory. Microsoft refresh tokens (if any) live here
/// too; API keys for AI are stored separately via <c>ISecretStore</c>.
/// </summary>
public sealed class AccountStore
{
    private readonly string _file;
    private readonly string _activeFile;

    public AccountStore(string settingsDir)
    {
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, "accounts.json");
        _activeFile = Path.Combine(settingsDir, "active_account.txt");
    }

    public List<Account> LoadAll()
    {
        if (!File.Exists(_file)) return new List<Account>();
        try
        {
            string json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
        }
        catch { return new List<Account>(); }
    }

    public void Save(List<Account> accounts)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_file, JsonSerializer.Serialize(accounts, opts));
    }

    public string? GetActiveUuid() =>
        File.Exists(_activeFile) ? File.ReadAllText(_activeFile).Trim() : null;

    public void SetActiveUuid(string uuid) => File.WriteAllText(_activeFile, uuid);
}
