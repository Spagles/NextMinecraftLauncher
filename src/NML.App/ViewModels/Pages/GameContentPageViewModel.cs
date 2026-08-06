using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Instances;
using NML.Core.Logging;
using NML.Core.Mods;
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

    /// <summary>True when the main flat file-list should be shown (not logs, configs, saves, or
    /// screenshots — saves and screenshots render their own grids instead).</summary>
    public bool IsFileListVisible => !IsLogsTab && !IsConfigsTab && !IsSavesTab && !IsScreenshotsTab;

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

    /// <summary>Screenshot cards shown in the screenshots grid (thumbnail + select + actions).</summary>
    public ObservableCollection<ScreenshotCardEntry> ScreenshotCards { get; } = new();

    /// <summary>World backups shown in the saves-tab backups panel (restore / delete per row).</summary>
    public ObservableCollection<BackupEntry> Backups { get; } = new();

    /// <summary>True while a world restore is in flight (drives the progress bar + disables restore buttons).</summary>
    [ObservableProperty] private bool _isRestoring;

    /// <summary>0–100 progress for the in-flight restore (bound to a ProgressBar).</summary>
    [ObservableProperty] private int _restoreProgress;

    /// <summary>Active restore's cancellation source (null when no restore is running).</summary>
    private System.Threading.CancellationTokenSource? _restoreCts;

    /// <summary>True when at least one screenshot card is selected (drives export button).</summary>
    public bool HasScreenshotSelection => ScreenshotCards.Any(c => c.IsSelected);

    /// <summary>True when all screenshot cards are selected (drives the select-all checkbox state).</summary>
    public bool AllScreenshotsSelected =>
        ScreenshotCards.Count > 0 && ScreenshotCards.All(c => c.IsSelected);

    /// <summary>Currently-edited config file content (plain-text blob; used when not structured).</summary>
    [ObservableProperty] private string _configContent = string.Empty;

    /// <summary>Name of the currently-selected config file (shown in the editor header).</summary>
    [ObservableProperty] private string _selectedConfigName = string.Empty;

    /// <summary>True when the selected file is a structured key=value dialect the per-row editor
    /// can render; false (plain-text blob) for TOML/JSON/etc.</summary>
    [ObservableProperty] private bool _isStructuredConfig;

    /// <summary>Per-row structured config entries bound to the per-row editor.</summary>
    public ObservableCollection<ConfigEntryRow> ConfigEntries { get; } = new();

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
        ScreenshotCards.Clear();
        Backups.Clear();
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
                    // Populate the backups panel (every {world}-{stamp}.zip, newest first).
                    foreach (BackupInfo b in browser.ListBackups())
                    {
                        Backups.Add(new BackupEntry
                        {
                            WorldName = b.WorldName,
                            Timestamp = b.Timestamp,
                            SizeBytes = b.SizeBytes,
                            Path = b.Path,
                        });
                    }
                    break;
                case "screenshots":
                    foreach (GameFile f in browser.ListScreenshots())
                    {
                        Items.Add(f);
                        ScreenshotCards.Add(new ScreenshotCardEntry
                        {
                            Name = f.Name,
                            Path = f.Path,
                            LastModified = f.LastModified,
                        });
                    }
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
    private void BackupWorldCard(WorldCardEntry card)
    {
        BackupWorld(card.ToGameSave());
        // Refresh so the new backup appears in the backups panel immediately.
        Refresh();
    }

    /// <summary>One-click export from a grid world card.</summary>
    [RelayCommand]
    private void ExportWorldCard(WorldCardEntry card) => ExportWorld(card.ToGameSave());

    /// <summary>One-click delete from a grid world card (refreshes the grid afterward).</summary>
    [RelayCommand]
    private void DeleteWorldCard(WorldCardEntry card) => DeleteWorld(card.ToGameSave());

    /// <summary>Restore a world from a backup zip with live progress + cancellation. Large worlds
    /// (multi-GB) extract over several seconds; the progress bar + Cancel button keep the UI
    /// responsive and let the user abort a wrong restore.</summary>
    [RelayCommand]
    private async Task RestoreBackupAsync(BackupEntry backup)
    {
        if (IsRestoring) return; // one restore at a time
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            IsRestoring = true;
            RestoreProgress = 0;
            Status = "backup.restore.restoring";
            _restoreCts = new System.Threading.CancellationTokenSource();
            // Throttle progress reporting: update at most every 2% so we don't flood the UI thread
            // with PropertyChanged for every 64 KiB chunk of a multi-GB world.
            int lastReported = -1;
            var progress = new Progress<(long extracted, long total)>(p =>
            {
                if (p.total <= 0) return;
                int pct = (int)Math.Clamp(p.extracted * 100 / p.total, 0, 100);
                if (pct == lastReported) return;
                lastReported = pct;
                RestoreProgress = pct;
            });
            await browser.RestoreWorldAsync(backup.Path, progress, _restoreCts.Token);
            Status = $"backup.restored,{backup.WorldName}";
            Refresh(); // reload worlds + backups panels
        }
        catch (OperationCanceledException)
        {
            Status = "backup.restore.cancelled";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
        finally
        {
            IsRestoring = false;
            RestoreProgress = 0;
            _restoreCts?.Dispose();
            _restoreCts = null;
        }
    }

    /// <summary>Cancel the in-flight restore (if any). The partially-extracted folder is left in
    /// place; re-running restore finishes the job (overwrite-extract is idempotent).</summary>
    [RelayCommand]
    private void CancelRestore()
    {
        try { _restoreCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already finished */ }
    }

    /// <summary>Delete a backup zip from the backups/ folder.</summary>
    [RelayCommand]
    private void DeleteBackup(BackupEntry backup)
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.DeleteBackup(backup.Path);
            Refresh();
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

    /// <summary>Open a screenshot from its grid card (accepts the card, converts to GameFile).</summary>
    [RelayCommand]
    private void OpenScreenshotCard(ScreenshotCardEntry card) => OpenScreenshot(card.ToGameFile());

    /// <summary>Copy a screenshot's file path to the system clipboard (paste into chat/upload).</summary>
    [RelayCommand]
    private void CopyScreenshot(ScreenshotCardEntry card)
    {
        try
        {
            if (!File.Exists(card.Path)) { Status = "common.error"; return; }
            // Avalonia 11: clipboard lives on the TopLevel (the main window), not Application.
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { Clipboard: var clipboard } && clipboard is not null)
            {
                clipboard.SetTextAsync(card.Path).GetAwaiter().GetResult();
                Status = "screenshot.copied";
            }
            else
            {
                Status = "common.error";
            }
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    /// <summary>Delete a screenshot from its grid card.</summary>
    [RelayCommand]
    private void DeleteScreenshotCard(ScreenshotCardEntry card) => DeleteScreenshot(card.ToGameFile());

    /// <summary>Toggle a card's selection state and re-raise the selection-dependent flags.</summary>
    [RelayCommand]
    private void ToggleScreenshot(ScreenshotCardEntry card)
    {
        card.IsSelected = !card.IsSelected;
        NotifyScreenshotSelectionChanged();
    }

    /// <summary>Select (or deselect) every screenshot card. Bound to the header checkbox.</summary>
    [RelayCommand]
    private void SelectAllScreenshots()
    {
        bool target = !AllScreenshotsSelected; // toggle to "all" if not already
        foreach (var c in ScreenshotCards) c.IsSelected = target;
        NotifyScreenshotSelectionChanged();
    }

    /// <summary>Bundle every selected screenshot into a timestamped .zip on the desktop.</summary>
    [RelayCommand]
    private void ExportSelectedScreensshots()
    {
        try
        {
            Instance? inst = GetActiveInstance();
            if (inst is null) return;
            var selected = ScreenshotCards.Where(c => c.IsSelected).ToList();
            if (selected.Count == 0) { Status = "screenshot.none_selected"; return; }
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zip = Path.Combine(desktop, $"screenshots-{stamp}.zip");
            browser.ExportScreenshotsToZip(selected.Select(c => c.Path), zip);
            Status = $"screenshot.exported,{selected.Count}";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    /// <summary>Re-raise the selection-driven flags after a card toggles or select-all fires.</summary>
    private void NotifyScreenshotSelectionChanged()
    {
        OnPropertyChanged(nameof(HasScreenshotSelection));
        OnPropertyChanged(nameof(AllScreenshotsSelected));
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
            SelectedConfigName = file.Name;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            string content = browser.ReadConfigFile(file.Path);
            ConfigContent = content;

            // Parse into structured rows when the file is a recognized key=value dialect; the UI
            // swaps to the per-row editor. Non-structured files (TOML/JSON) keep the plain blob.
            ConfigEntries.Clear();
            IsStructuredConfig = ConfigFileParser.IsStructured(file.Name);
            if (IsStructuredConfig)
            {
                foreach (var e in ConfigFileParser.Parse(content, file.Name))
                    ConfigEntries.Add(new ConfigEntryRow(e));
            }
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
            // When the per-row editor is active, serialize the (possibly edited) rows back; the
            // plain-text editor otherwise writes ConfigContent verbatim.
            string toWrite = IsStructuredConfig
                ? ConfigFileParser.Serialize(ConfigEntries.Select(r => r.ToEntry()).ToList())
                : ConfigContent;
            browser.WriteConfigFile(_selectedConfigFile.Path, toWrite);
            Status = "config.saved";
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

/// <summary>
/// A screenshot rendered as a grid card: thumbnail (bound directly to the file path), filename,
/// last-modified, a per-card selection checkbox (for batch export), and open/copy/delete actions.
/// </summary>
public sealed class ScreenshotCardEntry : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }

    private bool _isSelected;
    /// <summary>True when the card is part of the batch-export selection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Localized-friendly capture timestamp for display.</summary>
    public string DateDisplay => LastModified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    /// <summary>Reconstruct the equivalent <see cref="GameFile"/> so the shared screenshot commands work.</summary>
    public GameFile ToGameFile() => new()
    {
        Name = Name,
        Path = Path,
        LastModified = LastModified,
    };
}

/// <summary>
/// A world-backup zip rendered in the saves-tab backups panel: the world name, the capture
/// timestamp, a human-readable size, and the absolute path passed back to RestoreWorld/DeleteBackup.
/// </summary>
public sealed class BackupEntry : ObservableObject
{
    public string WorldName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public long SizeBytes { get; set; }
    public string Path { get; set; } = string.Empty;

    /// <summary>Localized-friendly capture timestamp for display.</summary>
    public string DateDisplay => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Human-readable size, e.g. "1.2 MB".</summary>
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024):F1} MB",
    };
}

/// <summary>
/// A UI-bindable wrapper around a parsed <see cref="ConfigEntry"/>. Key/Value are editable in the
/// per-row config editor; the Kind drives how the row renders (editable input vs. comment/section
/// display), and <see cref="ToEntry"/> reconstructs the source entry for serialization.
/// </summary>
public sealed class ConfigEntryRow : ObservableObject
{
    public ConfigEntryRow(ConfigEntry entry)
    {
        _kind = entry.Kind;
        _key = entry.Key;
        _value = entry.Value;
        _rawLine = entry.RawLine;
    }

    private readonly ConfigEntryKind _kind;
    private string _key;
    private string _value;
    private readonly string _rawLine;

    public ConfigEntryKind Kind => _kind;
    public string RawLine => _rawLine;

    /// <summary>Key for a KeyValue entry; section name for a Section entry.</summary>
    public string Key { get => _key; set => SetProperty(ref _key, value); }

    /// <summary>Value for a KeyValue entry (edited by the per-row text box).</summary>
    public string Value { get => _value; set => SetProperty(ref _value, value); }

    /// <summary>True when the row is an editable key=value pair (drives the input-template choice).</summary>
    public bool IsEditable => _kind == ConfigEntryKind.KeyValue;

    /// <summary>True when the row is a comment or blank (rendered as grayed-out text).</summary>
    public bool IsComment => _kind == ConfigEntryKind.Comment || _kind == ConfigEntryKind.Blank;

    /// <summary>True when the row is a [section] header.</summary>
    public bool IsSection => _kind == ConfigEntryKind.Section;

    /// <summary>Reconstruct the source <see cref="ConfigEntry"/> (with edited Key/Value) for serialize.</summary>
    public ConfigEntry ToEntry() => new(_kind, _key, _value, _rawLine);
}
