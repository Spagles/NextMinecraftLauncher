using System.Text.Json;
using NML.AICore;

namespace NML.App.Services;

/// <summary>
/// The launcher's persisted settings: which AI provider is active, the configured
/// providers (minus secrets — those live in the secret store), and the .minecraft path.
/// Stored as <c>settings.json</c> in the launcher's settings directory.
/// </summary>
public sealed class LauncherSettings
{
    /// <summary>The active AI provider's display name (or null = AI disabled).</summary>
    public string? ActiveProviderName { get; set; }

    /// <summary>Configured providers (API keys are NOT persisted here; loaded separately).</summary>
    public List<ChatProviderConfig> Providers { get; set; } = new();

    /// <summary>Path to the shared <c>.minecraft</c> directory (or null = use default).</summary>
    public string? MinecraftRoot { get; set; }

    /// <summary>Custom background image path for the launcher window (PCL-style). Null = default dark background.</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>Custom accent color as hex (e.g. "#4fc3f7"). Null = default.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Launcher settings directory (where this file, instances.json, secrets live).</summary>
    public string SettingsDir { get; set; } = string.Empty;
}

/// <summary>
/// Loads and saves <see cref="LauncherSettings"/> to <c>settings.json</c>.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _file;

    public SettingsStore(string settingsDir)
    {
        SettingsDir = settingsDir;
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, "settings.json");
    }

    public string SettingsDir { get; }

    public LauncherSettings Load()
    {
        if (!File.Exists(_file))
            return new LauncherSettings { SettingsDir = SettingsDir };

        try
        {
            string json = File.ReadAllText(_file);
            var s = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            s.SettingsDir = SettingsDir;
            return s;
        }
        catch
        {
            return new LauncherSettings { SettingsDir = SettingsDir };
        }
    }

    public void Save(LauncherSettings settings)
    {
        settings.SettingsDir = SettingsDir;
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_file, JsonSerializer.Serialize(settings, opts));
    }
}
