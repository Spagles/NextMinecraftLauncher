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

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Home/launch page: shows the user's instances, lets them pick one and launch it.
/// Reuses the full engine pipeline (VersionInfo → Java → command → process) and auto-runs
/// the crash analyzer on non-zero exit.
/// </summary>
public partial class HomePageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.home";
    public override string Icon => "🏠";

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
    private readonly ILogger<HomePageViewModel> _logger;

    public ObservableCollection<Instance> Instances { get; } = new();

    [ObservableProperty] private Instance? _selectedInstance;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _offlineUsername = "Player";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _installProgressPercent;

    public HomePageViewModel(
        VersionManifestService manifest,
        VanillaInstaller vanillaInstaller,
        VersionInfoService versions,
        JavaRuntimeDetector javaDetector,
        LaunchCommandBuilder launcher,
        ProcessLauncher processLauncher,
        InstanceStore instances,
        IOfflineAuthProvider offline,
        SettingsStore settings,
        ILogger<HomePageViewModel> logger,
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
        Status = "home.status_ready";

        foreach (Instance inst in _instances.LoadAll()) Instances.Add(inst);
    }

    public override Task OnNavigatedToAsync()
    {
        // Refresh the instance list in case another page added one.
        if (Instances.Count != _instances.LoadAll().Count)
        {
            Instances.Clear();
            foreach (Instance inst in _instances.LoadAll()) Instances.Add(inst);
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedInstance is null) { Status = "home.select_first"; return; }
        Instance inst = SelectedInstance;
        var mc = new MinecraftDirectory(_instances.GameDirFor(inst.Name));
        Directory.CreateDirectory(mc.Root);

        IsBusy = true;
        Status = "home.status_ready";
        try
        {
            VersionInfo version = await _versions.GetAsync(inst.VersionId, mc);

            List<JavaRuntime> runtimes = _javaDetector.DetectAll();
            int requiredMajor = version.JavaVersion?.MajorVersion ?? 17;
            JavaRuntime? java = inst.Java
                             ?? _javaDetector.FindForVersion(requiredMajor, runtimes)
                             ?? runtimes.FirstOrDefault();
            if (java is null) { Status = $"home.no_java,{requiredMajor}"; return; }

            Account account = _offline.Create(OfflineUsername);

            var opts = new LaunchOptions
            {
                Version = version, Mc = mc, Account = account, Java = java,
                MinMemoryMb = inst.MinMemoryMb, MaxMemoryMb = inst.MaxMemoryMb,
                WindowWidth = inst.WindowWidth, WindowHeight = inst.WindowHeight,
            };
            List<string> argv = _launcher.Build(opts);

            string logFile = Path.Combine(mc.Root, "logs", $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            Process process = _processLauncher.Launch(opts, argv, logFile);
            Status = $"home.launched,{inst.VersionId},{process.Id}";

            await process.WaitForExitAsync();
            Status = process.ExitCode != 0 ? $"home.crashed,{process.ExitCode}" : "home.clean_exit";
            if (process.ExitCode != 0) await DiagnoseCrashAsync(logFile);
        }
        catch (Exception ex)
        {
            Status = $"home.launch_failed,{ex.Message}";
            _logger.LogError(ex, "Launch failed.");
        }
        finally { IsBusy = false; }
    }

    private async Task DiagnoseCrashAsync(string launchLogPath)
    {
        if (_crashFactory?.TryCreate() is not { } analyzer) return;
        string? crashText = File.Exists(launchLogPath) ? File.ReadAllText(launchLogPath) : null;
        if (crashText is null) return;
        try
        {
            Status = "home.diagnosing";
            var d = await analyzer.AnalyzeAsync(crashText);
            Status = $"diagnosis|{d.Confidence}|{d.RootCause}";
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Crash diagnosis failed."); }
    }
}
