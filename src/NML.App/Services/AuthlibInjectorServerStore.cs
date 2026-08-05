using System.Text.Json;
using NML.Core.Auth.AuthlibInjector;

namespace NML.App.Services;

/// <summary>
/// Persists the user's list of authlib-injector (external Yggdrasil) login servers as JSON.
/// Lets the user save known servers (LittleSkin, etc.) once and pick from a dropdown on login,
/// rather than typing the API URL every time. Mirrors HMCL's server-management panel.
/// </summary>
public sealed class AuthlibInjectorServerStore
{
    private readonly string _file;
    private readonly string _activeFile;

    public AuthlibInjectorServerStore(string settingsDir)
    {
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, "authlib_servers.json");
        _activeFile = Path.Combine(settingsDir, "active_authlib_server.txt");
    }

    /// <summary>Load all saved servers (metadata not yet resolved).</summary>
    public List<AuthlibInjectorServer> LoadAll()
    {
        if (!File.Exists(_file)) return new List<AuthlibInjectorServer>();
        try
        {
            string json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<List<AuthlibInjectorServer>>(json) ?? new List<AuthlibInjectorServer>();
        }
        catch { return new List<AuthlibInjectorServer>(); }
    }

    /// <summary>Persist the full list.</summary>
    public void Save(IEnumerable<AuthlibInjectorServer> servers)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_file, JsonSerializer.Serialize(servers.ToList(), opts));
    }

    /// <summary>Add a new server, replacing any with the same ApiUrl.</summary>
    public void Add(AuthlibInjectorServer server)
    {
        var all = LoadAll();
        all.RemoveAll(s => string.Equals(s.ApiUrl, server.ApiUrl, StringComparison.OrdinalIgnoreCase));
        all.Add(server);
        Save(all);
    }

    /// <summary>Remove a server by API URL.</summary>
    public void Remove(string apiUrl)
    {
        var all = LoadAll();
        all.RemoveAll(s => string.Equals(s.ApiUrl, apiUrl, StringComparison.OrdinalIgnoreCase));
        Save(all);
    }

    /// <summary>The active server's API URL (the one used at login time), or null.</summary>
    public string? GetActiveApiUrl() =>
        File.Exists(_activeFile) ? File.ReadAllText(_activeFile).Trim() : null;

    public void SetActiveApiUrl(string? apiUrl)
    {
        if (string.IsNullOrEmpty(apiUrl))
        {
            if (File.Exists(_activeFile)) File.Delete(_activeFile);
        }
        else
        {
            File.WriteAllText(_activeFile, apiUrl);
        }
    }
}
