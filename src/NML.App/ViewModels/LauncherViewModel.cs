using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Auth;
using NML.Core.Download;
using NML.Core.Instances;
using NML.Core.Java;
using NML.Core.Launch;
using NML.Core.Models;

namespace NML.App.ViewModels;

/// <summary>
/// Main-window view model: shows installed instances, the latest available versions, and
/// exposes the launch flow (install → resolve Java → build command → start process).
/// Also surfaces the AI crash-diagnosis hook when a launch exits non-zero.
/// </summary>
public partial class LauncherViewModel : ObservableObject
{
    private readonly VersionManifestService _manifest;
    private readonly VanillaInstaller _vanillaInstaller;
    private readonly VersionInfoService _versions;
    private readonly JavaRuntimeDetector _javaDetector;
    private readonly LaunchCommandBuilder _launcher;
    private readonly ProcessLauncher _processLauncher;
    private readonly InstanceStore _instances;
    private readonly IOfflineAuthProvider _offline;
    private readonly SettingsStore _settings;
    private readonly CrashAnalyzerFactory? _crashFactory;
    private readonly ILogger<LauncherViewModel> _logger;

    public ObservableCollection<Instance> Instances { get; } = new();
    public ObservableCollection<VersionManifestEntry> AvailableVersions { get; } = new();

    [ObservableProperty] private Instance? _selectedInstance;
    [ObservableProperty] private VersionManifestEntry? _selectedVersionToInstall;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private string _offlineUsername = "Player";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _installProgressPercent;
    [ObservableProperty] private string _lastDiagnosis = string.Empty;

    /// <summary>True when at least one instance is configured (drives the empty-state hint).</summary>
    public bool HasInstances => Instances.Count > 0;

    private void RefreshHasInstances() => OnPropertyChanged(nameof(HasInstances));

    public LauncherViewModel(
        VersionManifestService manifest,
        VanillaInstaller vanillaInstaller,
        VersionInfoService versions,
        JavaRuntimeDetector javaDetector,
        LaunchCommandBuilder launcher,
        ProcessLauncher processLauncher,
        InstanceStore instances,
        IOfflineAuthProvider offline,
        SettingsStore settings,
        ILogger<LauncherViewModel> logger,
        CrashAnalyzerFactory? crashFactory = null)
    {
        _manifest = manifest;
        _vanillaInstaller = vanillaInstaller;
        _versions = versions;
        _javaDetector = javaDetector;
        _launcher = launcher;
        _processLauncher = processLauncher;
        _instances = instances;
        _offline = offline;
        _settings = settings;
        _crashFactory = crashFactory;
        _logger = logger;

        // Load saved instances immediately.
        foreach (Instance inst in _instances.LoadAll()) Instances.Add(inst);
        Instances.CollectionChanged += (_, _) => RefreshHasInstances();
    }

    /// <summary>Fetch the Mojang version manifest and populate <see cref="AvailableVersions"/>.</summary>
    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        Status = "Fetching version list…";
        try
        {
            VersionManifest m = await _manifest.GetAsync();
            AvailableVersions.Clear();
            foreach (VersionManifestEntry v in m.Versions.Take(50))
                AvailableVersions.Add(v);
            Status = $"Loaded {m.Versions.Count} versions (showing top 50).";
        }
        catch (Exception ex)
        {
            Status = $"Failed to load versions: {ex.Message}";
            _logger.LogError(ex, "Version list fetch failed.");
        }
        finally { IsBusy = false; }
    }

    /// <summary>Install the currently-selected version as a new instance.</summary>
    [RelayCommand]
    private async Task InstallSelectedVersionAsync()
    {
        if (SelectedVersionToInstall is null)
        {
            Status = "Pick a version to install first.";
            return;
        }

        string versionId = SelectedVersionToInstall.Id;
        string name = $"{versionId} (vanilla)";
        var instance = new Instance { Name = name, VersionId = versionId, MaxMemoryMb = 4096 };
        var mc = new MinecraftDirectory(_instances.GameDirFor(name));

        IsBusy = true;
        InstallProgressPercent = 0;
        Status = $"Installing {versionId}…";
        try
        {
            await _vanillaInstaller.InstallAsync(versionId, mc, ruleCtx: null, cancel: null,
                progress: ReportInstallProgress,
                downloadSettings: _settings.ResolveDownloadSettings(_manifest));
            _instances.Add(instance);
            Instances.Add(instance);
            SelectedInstance = instance;
            Status = $"Installed {versionId}. Ready to launch.";
        }
        catch (Exception ex)
        {
            Status = $"Install failed: {ex.Message}";
            _logger.LogError(ex, "Install of {Id} failed.", versionId);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Launch the selected instance with the offline account. Runs the full engine pipeline.</summary>
    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedInstance is null)
        {
            Status = "Select an instance to launch.";
            return;
        }

        Instance inst = SelectedInstance;
        var mc = new MinecraftDirectory(_instances.GameDirFor(inst.Name));
        Directory.CreateDirectory(mc.Root);

        IsBusy = true;
        Status = "Preparing launch…";
        try
        {
            // 1. Resolve the version metadata (handles inheritsFrom internally).
            VersionInfo version = await _versions.GetAsync(inst.VersionId, mc);

            // 2. Detect a suitable Java runtime.
            List<JavaRuntime> runtimes = _javaDetector.DetectAll();
            int requiredMajor = version.JavaVersion?.MajorVersion ?? 17;
            JavaRuntime? java = inst.Java
                             ?? _javaDetector.FindForVersion(requiredMajor, runtimes)
                             ?? runtimes.FirstOrDefault();
            if (java is null)
                throw new InvalidOperationException(
                    $"No Java {requiredMajor}+ runtime found. Install Java or let the launcher fetch one.");

            // 3. Build the account (offline for now; online flow would go through MicrosoftAuthProvider).
            Account account = _offline.Create(OfflineUsername);

            // 4. Build the launch command and spawn the process.
            var opts = new LaunchOptions
            {
                Version = version,
                Mc = mc,
                Account = account,
                Java = java,
                MinMemoryMb = inst.MinMemoryMb,
                MaxMemoryMb = inst.MaxMemoryMb,
                WindowWidth = inst.WindowWidth,
                WindowHeight = inst.WindowHeight,
            };
            List<string> argv = _launcher.Build(opts);

            string logFile = Path.Combine(mc.Root, "logs", $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            Process process = _processLauncher.Launch(opts, argv, logFile);
            Status = $"Launched {inst.VersionId} (PID {process.Id}). Waiting for exit…";

            // 5. Await exit; if it crashed, offer AI diagnosis.
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Status = $"Game exited with code {process.ExitCode} (possible crash).";
                await DiagnoseCrashAsync(logFile);
            }
            else
            {
                Status = "Game exited cleanly.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Launch failed: {ex.Message}";
            _logger.LogError(ex, "Launch of {Name} failed.", inst.Name);
        }
        finally { IsBusy = false; }
    }

    /// <summary>If a crash log exists and an AI provider is configured, run the crash analyzer.</summary>
    private async Task DiagnoseCrashAsync(string launchLogPath)
    {
        if (_crashFactory?.TryCreate() is not { } analyzer)
        {
            _logger.LogInformation("No AI provider configured; skipping crash diagnosis.");
            return;
        }

        string? crashText = ReadCrashReport(launchLogPath);
        if (crashText is null) return;

        try
        {
            Status = "Diagnosing crash with AI…";
            var diagnosis = await analyzer.AnalyzeAsync(crashText, logTail: null);
            LastDiagnosis = string.IsNullOrEmpty(diagnosis.RootCause)
                ? diagnosis.RawNarrative
                : $"[{diagnosis.Confidence}] {diagnosis.RootCause}\nFixes:\n - " +
                  string.Join("\n - ", diagnosis.LikelyFixes);
            Status = "Crash diagnosis ready.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI crash diagnosis failed.");
            Status = "Crash diagnosis unavailable (AI call failed).";
        }
    }

    /// <summary>Pull the captured log as the text to diagnose.</summary>
    private static string? ReadCrashReport(string launchLogPath)
    {
        try { return File.Exists(launchLogPath) ? File.ReadAllText(launchLogPath) : null; }
        catch { return null; }
    }

    /// <summary>Progress callback for installs; updates <see cref="InstallProgressPercent"/>.</summary>
    private void ReportInstallProgress(in DownloadProgress progress, string currentFileName)
    {
        // Map the fraction (NaN when unknown) to a percentage.
        double frac = progress.TotalFiles > 0 ? progress.FileFraction : progress.Fraction;
        if (!double.IsNaN(frac))
            InstallProgressPercent = (int)Math.Clamp(frac * 100, 0, 100);
        if (!string.IsNullOrEmpty(currentFileName))
            Status = $"Installing… {Path.GetFileName(currentFileName)}";
    }

    /// <summary>Delete the selected instance (removes from list + store).</summary>
    [RelayCommand]
    private void RemoveInstance(Instance instance)
    {
        Instances.Remove(instance);
        _instances.Remove(instance.Name);
        Status = $"Removed instance '{instance.Name}'.";
    }
}
