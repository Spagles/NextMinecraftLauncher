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

    /// <summary>UI theme: "dark", "light", or "system". Null = dark.</summary>
    public string? Theme { get; set; }

    /// <summary>Max simultaneous downloads for version installs (1–64). Null = default (8).</summary>
    public int? DownloadConcurrency { get; set; }

    /// <summary>Mirror base URL to route Mojang asset downloads through (BMCLAPI-style), or
    /// null/empty = official Mojang endpoints. Example: <c>https://bmclapi2.bangbang93.com</c>.</summary>
    public string? DownloadMirrorUrl { get; set; }

    /// <summary>Launcher settings directory (where this file, instances.json, secrets live).</summary>
    public string SettingsDir { get; set; } = string.Empty;

    /// <summary>Memory preset name: "auto", "low", "medium", "high", or "custom".</summary>
    public string? MemoryPreset { get; set; }

    /// <summary>Launch behavior: "normal" (stay open), "minimize" (minimize after launch), "close" (close after launch).</summary>
    public string? LaunchBehavior { get; set; }

    /// <summary>UI font size scale: 0.9 (small), 1.0 (normal), 1.1 (large), 1.2 (extra large).</summary>
    public double? FontScale { get; set; } = 1.0;

    /// <summary>True to check GitHub Releases for a newer launcher version on startup (non-blocking).</summary>
    public bool? CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>True to auto-backup the active instance's worlds periodically while a game is running, plus a final backup on exit.</summary>
    public bool? AutoBackupWorlds { get; set; }

    /// <summary>Auto-backup interval in minutes (only when a game is running). Default 30.</summary>
    public int? AutoBackupIntervalMinutes { get; set; } = 30;

    /// <summary>Max auto-backup zips to keep per instance (oldest pruned). 0 = no pruning. Default 10.</summary>
    public int? AutoBackupKeepCount { get; set; } = 10;
}

/// <summary>
/// Loads and saves <see cref="LauncherSettings"/> to <c>settings.json</c>.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _file;
    private readonly object _lock = new();

    public SettingsStore(string settingsDir)
    {
        SettingsDir = settingsDir;
        Directory.CreateDirectory(settingsDir);
        _file = Path.Combine(settingsDir, "settings.json");
    }

    public string SettingsDir { get; }

    public LauncherSettings Load()
    {
        lock (_lock)
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
    }

    public void Save(LauncherSettings settings)
    {
        lock (_lock)
        {
            settings.SettingsDir = SettingsDir;
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_file, JsonSerializer.Serialize(settings, opts));
        }
    }

    /// <summary>
    /// Build a <see cref="Core.Download.DownloadSettings"/> from the current saved settings,
    /// threading the user's <c>DownloadMirrorUrl</c> + <c>DownloadConcurrency</c> into the value
    /// the install pipeline actually consumes. Callers should invoke this fresh on every install
    /// so edits made on the settings page take effect immediately (the persisted settings.json is
    /// the single source of truth). Also pushes the mirror into the manifest service so the
    /// version_manifest + version.json fetches are mirror-aware.
    /// </summary>
    public NML.Core.Download.DownloadSettings ResolveDownloadSettings(
        NML.Core.VersionManifestService? manifest = null)
    {
        LauncherSettings s = Load();
        var mirror = string.IsNullOrWhiteSpace(s.DownloadMirrorUrl) ? null : s.DownloadMirrorUrl;
        // Keep the manifest service in sync so its next fetch (manifest + version.json) uses the
        // same mirror as the bulk library/asset downloads.
        if (manifest is not null) manifest.MirrorUrl = mirror;
        return new NML.Core.Download.DownloadSettings
        {
            MirrorUrl = mirror,
            Concurrency = s.DownloadConcurrency ?? NML.Core.Download.DownloadSettings.DefaultConcurrency,
        };
    }
}
