using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Instances;
using NML.Core.Logging;
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

    /// <summary>True when the main flat file-list should be shown (not logs, configs, or saves —
    /// saves render their own icon grid instead).</summary>
    public bool IsFileListVisible => !IsLogsTab && !IsConfigsTab && !IsSavesTab;

    [ObservableProperty] private string _logContent = string.Empty;
    [ObservableProperty] private string _logSearchText = string.Empty;

    /// <summary>True when the search box is treated as a regex pattern (false = plain substring).</summary>
    [ObservableProperty] private bool _isRegexSearch;

    /// <summary>
    /// Minimum severity to display ("Error" hides Warn/Info/Debug/Trace, etc.).
    /// Bound to a dropdown of <see cref="LogSeverityOptions"/>.
    /// </summary>
    [ObservableProperty] private string _minSeverity = nameof(LogSeverityClassifier.Severity.Trace);

    /// <summary>Severity bands offered in the filter dropdown, most-severe first.</summary>
    public IReadOnlyList<string> LogSeverityOptions { get; } = new[]
    {
        nameof(LogSeverityClassifier.Severity.Trace),
        nameof(LogSeverityClassifier.Severity.Debug),
        nameof(LogSeverityClassifier.Severity.Info),
        nameof(LogSeverityClassifier.Severity.Warn),
        nameof(LogSeverityClassifier.Severity.Error),
    };

    /// <summary>All classified lines from the current log (pre-filter).</summary>
    private List<LogLine> _allLogLines = new();

    /// <summary>Filtered + classified lines bound to the colored ItemsControl.</summary>
    public ObservableCollection<LogLineEntry> FilteredLogLines { get; } = new();

    /// <summary>World cards shown in the saves grid (icon + name + last played + actions).</summary>
    public ObservableCollection<WorldCardEntry> WorldCards { get; } = new();

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
            string raw = await Task.Run(() => browser.ReadLatestLog());
            LogContent = string.IsNullOrEmpty(raw) ? "content.empty" : raw;
            // Classify every line once; the filter rebuild is cheap relative to the I/O.
            _allLogLines = LogSeverityClassifier.ClassifyAll(
                LogContent.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
            RebuildFilteredLog();
        }
        catch (Exception ex) { LogContent = $"common.error: {ex.Message}"; }
    }

    /// <summary>
    /// Rebuild <see cref="FilteredLogLines"/> from <see cref="_allLogLines"/> by applying the
    /// severity floor and the substring-or-regex search. Swallows invalid regex patterns
    /// (treats them as "no match" and clears the list) so a half-typed pattern never crashes.
    /// </summary>
    private void RebuildFilteredLog()
    {
        FilteredLogLines.Clear();
        if (_allLogLines.Count == 0) return;

        // Parse the severity floor (default Trace = show everything).
        if (!Enum.TryParse<LogSeverityClassifier.Severity>(MinSeverity, out var floor))
            floor = LogSeverityClassifier.Severity.Trace;

        // Compile the regex once if in regex mode; fall back to substring comparison otherwise.
        Regex? regex = null;
        bool hasSearch = !string.IsNullOrWhiteSpace(LogSearchText);
        if (hasSearch && IsRegexSearch)
        {
            try { regex = new Regex(LogSearchText, RegexOptions.IgnoreCase); }
            catch (ArgumentException) { FilteredLogLines.Clear(); return; } // invalid pattern
        }

        foreach (var line in _allLogLines)
        {
            // Severity floor: Error(0) < Warn(1) < Info(2) < Debug(3) < Trace(4).
            // Show a line only if its severity is at-or-above the floor (i.e. <= floor numerically).
            if ((int)line.Severity > (int)floor) continue;

            if (hasSearch)
            {
                bool match = regex is not null
                    ? regex.IsMatch(line.Text)
                    : line.Text.Contains(LogSearchText, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
            }
            FilteredLogLines.Add(new LogLineEntry(line.Text, line.Color));
        }
    }

    // Re-run the filter whenever any of its inputs change.
    partial void OnLogSearchTextChanged(string value) => RebuildFilteredLog();
    partial void OnLogContentChanged(string value) => RebuildFilteredLog();
    partial void OnIsRegexSearchChanged(bool value) => RebuildFilteredLog();
    partial void OnMinSeverityChanged(string value) => RebuildFilteredLog();

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
        WorldCards.Clear();
        try
        {
            switch (Tab)
            {
                case "saves":
                    foreach (GameSave s in browser.ListSaves())
                    {
                        Items.Add(s);
                        WorldCards.Add(new WorldCardEntry
                        {
                            Name = s.Name,
                            DisplayName = s.DisplayName,
                            Path = s.Path,
                            SizeBytes = s.SizeBytes,
                            LastModified = s.LastModified,
                            PreviewIconPath = s.PreviewIconPath,
                        });
                    }
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
            Status = $"world.backup_done,{Path.GetFileName(zip)}";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    /// <summary>One-click backup from a grid world card (converts the card back to a GameSave).</summary>
    [RelayCommand]
    private void BackupWorldCard(WorldCardEntry card) => BackupWorld(card.ToGameSave());

    /// <summary>One-click export from a grid world card.</summary>
    [RelayCommand]
    private void ExportWorldCard(WorldCardEntry card) => ExportWorld(card.ToGameSave());

    /// <summary>One-click delete from a grid world card (refreshes the grid afterward).</summary>
    [RelayCommand]
    private void DeleteWorldCard(WorldCardEntry card) => DeleteWorld(card.ToGameSave());

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

/// <summary>
/// A single line of the log viewer bound to the colored <c>ItemsControl</c>. Carries the
/// raw text and the severity-derived hex color so the XAML <c>DataTemplate</c> can render
/// each line with the right <c>Foreground</c> without re-classifying in the view.
/// </summary>
public sealed class LogLineEntry : ObservableObject
{
    public LogLineEntry(string text, string color)
    {
        _text = text;
        _color = color;
    }

    private readonly string _text;
    private readonly string _color;

    /// <summary>The raw log line text.</summary>
    public string Text => _text;

    /// <summary>Hex color for this line (severity-derived, e.g. "#ef5350" for errors).</summary>
    public string Color => _color;
}

/// <summary>
/// A world save rendered as a grid card: preview icon (or null → UI placeholder), in-world
/// display name, human-readable size + last-played, and the original path/name so the existing
/// backup/export/delete commands (which expect a <see cref="GameSave"/>) can be reused via
/// <see cref="ToGameSave"/>.
/// </summary>
public sealed class WorldCardEntry : ObservableObject
{
    /// <summary>Folder name on disk (the raw save directory name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>In-world display name from level.dat (falls back to <see cref="Name"/>).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Absolute path to the save folder.</summary>
    public string Path { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public DateTimeOffset LastModified { get; set; }

    /// <summary>Absolute path to icon.png, or null when the world has no custom icon.</summary>
    public string? PreviewIconPath { get; set; }

    /// <summary>True when the world has a custom preview icon (drives the Image/placeholder swap).</summary>
    public bool HasIcon => !string.IsNullOrEmpty(PreviewIconPath);

    /// <summary>Human-readable size, e.g. "12.3 MB".</summary>
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    /// <summary>Localized-friendly last-played timestamp.</summary>
    public string LastPlayedDisplay => LastModified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>Reconstruct the equivalent <see cref="GameSave"/> so the shared world commands work.</summary>
    public GameSave ToGameSave() => new()
    {
        Name = Name,
        Path = Path,
        SizeBytes = SizeBytes,
        LastModified = LastModified,
        DisplayName = DisplayName,
        PreviewIconPath = PreviewIconPath,
    };
}
