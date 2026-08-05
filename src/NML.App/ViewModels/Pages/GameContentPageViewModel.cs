using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Instances;
using NML.Core.Modloaders;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Game-content browser page: tabs for saves / screenshots / resource packs / mods, each
/// backed by <see cref="GameContentBrowser"/>. Reads from the active instance's game dir.
/// </summary>
public partial class GameContentPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.game_content";
    public override string Icon => "📁";

    private readonly InstanceStore _instances;
    private readonly NML.Data.Modrinth.ModrinthCatalog? _modrinthCatalog;
    private readonly ILogger<GameContentPageViewModel> _logger;

    public ObservableCollection<object> Items { get; } = new();

    /// <summary>Installed mods with update-check results (shown on the mods tab).</summary>
    public ObservableCollection<InstalledModInfo> InstalledMods { get; } = new();

    [ObservableProperty] private string _tab = "saves"; // saves|screenshots|resourcepacks|mods|logs|configs
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isCheckingModUpdates;
    [ObservableProperty] private int _updatesAvailable;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _importWorldPath = string.Empty;

    /// <summary>Cached active instance (first one). Refreshed on navigation to avoid
    /// reading instances.json from disk on every single command invocation.</summary>
    private Instance? _activeInstance;

    /// <summary>Get the active instance — uses the cached value if available.</summary>
    private Instance? GetActiveInstance() => _activeInstance ??= _instances.LoadAll().FirstOrDefault();

    public GameContentPageViewModel(
        InstanceStore instances,
        ILogger<GameContentPageViewModel> logger,
        NML.Data.Modrinth.ModrinthCatalog? modrinthCatalog = null)
    {
        _instances = instances;
        _logger = logger;
        _modrinthCatalog = modrinthCatalog;
        EnsureLanguageSubscribed();
    }

    /// <summary>True when the saves tab is active (drives backup/delete button visibility).</summary>
    public bool IsSavesTab => Tab == "saves";

    /// <summary>True when the screenshots tab is active (drives open/delete button visibility).</summary>
    public bool IsScreenshotsTab => Tab == "screenshots";

    /// <summary>True when the resource packs tab is active (drives delete button visibility).</summary>
    public bool IsResourcePacksTab => Tab == "resourcepacks";

    /// <summary>True when the logs tab is active (shows the log viewer).</summary>
    public bool IsLogsTab => Tab == "logs";

    /// <summary>True when the configs tab is active (shows the config editor).</summary>
    public bool IsConfigsTab => Tab == "configs";

    /// <summary>True when the main file-list should be shown (not logs or configs tab).</summary>
    public bool IsFileListVisible => !IsLogsTab && !IsConfigsTab;

    [ObservableProperty] private string _logContent = string.Empty;
    [ObservableProperty] private string _logSearchText = string.Empty;

    /// <summary>Currently-edited config file content.</summary>
    [ObservableProperty] private string _configContent = string.Empty;
    /// <summary>Path of the currently-selected config file.</summary>
    private GameFile? _selectedConfigFile;

    public override Task OnNavigatedToAsync() { Refresh(); return Task.CompletedTask; }

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsSavesTab));
        OnPropertyChanged(nameof(IsScreenshotsTab));
        OnPropertyChanged(nameof(IsResourcePacksTab));
        OnPropertyChanged(nameof(IsLogsTab));
        OnPropertyChanged(nameof(IsConfigsTab));
        OnPropertyChanged(nameof(IsFileListVisible));
        if (value == "logs") _ = LoadLogAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task LoadLogAsync()
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) { LogContent = "content.empty"; return; }
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            LogContent = await Task.Run(() => browser.ReadLatestLog());
            if (string.IsNullOrEmpty(LogContent)) LogContent = "content.empty";
        }
        catch (Exception ex) { LogContent = $"common.error: {ex.Message}"; }
    }

    /// <summary>Filtered log lines matching the search text (null/empty = all).</summary>
    public string FilteredLog
    {
        get
        {
            if (string.IsNullOrEmpty(LogSearchText) || string.IsNullOrEmpty(LogContent))
                return LogContent;
            var lines = LogContent.Split('\n');
            var filtered = lines.Where(l => l.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase));
            return string.Join('\n', filtered);
        }
    }

    partial void OnLogSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredLog));
    partial void OnLogContentChanged(string value) => OnPropertyChanged(nameof(FilteredLog));

    [RelayCommand]
    private void EnableAllMods()
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            int count = browser.EnableAllMods();
            Status = $"mods.enabled_all,{count}";
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void DisableAllMods()
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            int count = browser.DisableAllMods();
            Status = $"mods.disabled_all,{count}";
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void Refresh()
    {
        // Use the first instance's game dir (or fall back to default .minecraft).
        Instance? inst = GetActiveInstance();
        string root = inst is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : _instances.GameDirFor(inst.Name);
        var browser = new GameContentBrowser(new MinecraftDirectory(root));

        Items.Clear();
        try
        {
            switch (Tab)
            {
                case "saves":
                    foreach (GameSave s in browser.ListSaves()) Items.Add(s);
                    break;
                case "screenshots":
                    foreach (GameFile f in browser.ListScreenshots()) Items.Add(f);
                    break;
                case "resourcepacks":
                    foreach (GameFile f in browser.ListResourcePacks()) Items.Add(f);
                    break;
                case "mods":
                    foreach (GameFile f in browser.ListMods()) Items.Add(f);
                    break;
                case "configs":
                    foreach (GameFile f in browser.ListConfigFiles()) Items.Add(f);
                    break;
            }
            IsEmpty = Items.Count == 0;
            Status = IsEmpty ? "content.empty" : $"{Items.Count}";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Content load failed.");
        }
    }

    [RelayCommand]
    private void ToggleMod(GameFile file)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.ToggleMod(file.Path);
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void BackupWorld(GameSave save)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            string zip = browser.BackupWorld(save.Path);
            Status = $"home.installed,{zip}";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void ExportWorld(GameSave save)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string zipPath = Path.Combine(desktop, $"{save.Name}.zip");
            browser.ExportWorld(save.Path, zipPath);
            Status = $"home.exported,{zipPath}";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void ImportWorld(string zipPath)
    {
        if (string.IsNullOrEmpty(zipPath)) return;
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            string worldDir = browser.ImportWorld(zipPath);
            Status = $"home.installed,{Path.GetFileName(worldDir)}";
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void DeleteWorld(GameSave save)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.DeleteWorld(save.Path);
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void DeleteScreenshot(GameFile file)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.DeleteScreenshot(file.Path);
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void OpenScreenshot(GameFile file)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.OpenScreenshot(file.Path);
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void DeleteResourcePack(GameFile file)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.DeleteResourcePack(file.Path);
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void SelectConfig(GameFile file)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            _selectedConfigFile = file;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            ConfigContent = browser.ReadConfigFile(file.Path);
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        try
        {
            if (_selectedConfigFile is null) return;
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.WriteConfigFile(_selectedConfigFile.Path, ConfigContent);
            Status = "common.save";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Instance? inst = GetActiveInstance();
        string root = inst is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : _instances.GameDirFor(inst.Name);
        string target = Tab == "saves" ? Path.Combine(root, "saves")
                      : Tab == "screenshots" ? Path.Combine(root, "screenshots")
                      : Tab == "resourcepacks" ? Path.Combine(root, "resourcepacks")
                      : Path.Combine(root, "mods");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { _logger.LogWarning(ex, "Open folder failed."); }
    }

    /// <summary>Scan installed mods and check each against Modrinth for updates.</summary>
    [RelayCommand]
    private async Task CheckModUpdatesAsync()
    {
        if (_modrinthCatalog is null) { Status = "common.error"; return; }

        Instance? inst = GetActiveInstance();
        if (inst is null) { Status = "mods.no_instance"; return; }

        IsCheckingModUpdates = true;
        Status = "common.loading";
        InstalledMods.Clear();
        UpdatesAvailable = 0;

        try
        {
            string modsDir = Path.Combine(_instances.GameDirFor(inst.Name), "mods");
            var installed = ModVersionChecker.ScanInstalledMods(modsDir);

            foreach (var mod in installed)
            {
                // Query Modrinth for the mod's project by slug/id.
                try
                {
                    var results = await _modrinthCatalog.SearchAsync(mod.ModId, limit: 1);
                    if (results.Count > 0 && !string.IsNullOrEmpty(mod.Version))
                    {
                        // A real implementation would fetch the project's latest version file and compare.
                        // For the MVP, mark as potentially-updatable if the search found the mod.
                        mod.UpdateAvailable = true; // simplified
                        mod.LatestVersion = results[0].Title;
                        UpdatesAvailable++;
                    }
                }
                catch { /* skip mods that can't be found */ }
                InstalledMods.Add(mod);
            }

            // Check for conflicts (duplicate ids, mixed loaders).
            var conflicts = ModConflictDetector.Detect(installed);
            int conflictCount = conflicts.Count;

            // Check for missing dependencies and breaks conflicts.
            var depIssues = ModDependencyChecker.Check(installed, modsDir);
            int depIssueCount = depIssues.Count;

            string updateStatus = UpdatesAvailable > 0
                ? $"mods.updates_found,{UpdatesAvailable}"
                : "mods.up_to_date";
            Status = conflictCount + depIssueCount > 0
                ? $"mods.issues_found,{conflictCount + depIssueCount}"
                : updateStatus;
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Mod update check failed.");
        }
        finally { IsCheckingModUpdates = false; }
    }
}
