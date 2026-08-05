using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Auth;
using NML.Core.Auth.AuthlibInjector;
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
    private readonly AuthlibInjectorSetup? _authlibInjectorSetup;
    private readonly AccountStore? _activeAccountStore;
    private readonly InstanceTransferService? _instanceTransfer;
    private readonly ILogger<HomePageViewModel> _logger;

    public ObservableCollection<Instance> Instances { get; } = new();

    [ObservableProperty] private Instance? _selectedInstance;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string _offlineUsername = "Player";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _installProgressPercent;

    /// <summary>System total RAM in MB (drives the slider max + recommended hint).</summary>
    public long SystemRamMb
    {
        get
        {
            try
            {
                // GCMemoryInfo.TotalAvailableMemoryBytes gives total physical RAM on most platforms.
                return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            }
            catch { return 0; }
        }
    }

    /// <summary>Max memory slider value (clamp to system RAM, min 1024).</summary>
    public long SliderMax => Math.Max(1024, SystemRamMb > 0 ? SystemRamMb : 16384);

    /// <summary>Recommended memory for the selected instance (2/3 of system, clamped 1024..SliderMax).</summary>
    public long RecommendedMemory => SystemRamMb > 0
        ? Math.Clamp((long)(SystemRamMb * 0.66), 1024, SliderMax)
        : 4096;

    /// <summary>Two-way bindable max-memory for the selected instance.</summary>
    public int SelectedMaxMemory
    {
        get => SelectedInstance?.MaxMemoryMb ?? 2048;
        set
        {
            if (SelectedInstance is not null)
            {
                SelectedInstance.MaxMemoryMb = value;
                OnPropertyChanged();
            }
        }
    }

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
        CrashAnalyzerFactory? crashFactory = null,
        AuthlibInjectorSetup? authlibInjectorSetup = null,
        AccountStore? activeAccountStore = null,
        InstanceTransferService? instanceTransfer = null)
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
        _authlibInjectorSetup = authlibInjectorSetup;
        _activeAccountStore = activeAccountStore;
        _instanceTransfer = instanceTransfer;
        _logger = logger;
        EnsureLanguageSubscribed();
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

            // Default to an offline account; if the active account is an authlib-injector
            // (external Yggdrasil) one, use it instead and prepare the java agent.
            Account account = _offline.Create(OfflineUsername);
            AuthlibInjectorServer? authlibServer = null;
            string? authlibJarPath = null;

            // Pull the active account from the AccountStore and, if it's external-login,
            // reconstruct the server + ensure the agent jar is cached before launching.
            Account? activeExternal = _activeAccountStore?.LoadAll()
                .FirstOrDefault(a => a.Uuid == _activeAccountStore?.GetActiveUuid()
                                     && a.AccountType == "authlib-injector");
            if (activeExternal is not null && _authlibInjectorSetup is not null
                && !string.IsNullOrEmpty(activeExternal.Xuid))
            {
                account = activeExternal;
                authlibServer = new AuthlibInjectorServer
                {
                    Name = activeExternal.Username,
                    ApiUrl = activeExternal.Xuid, // server URL is stashed here on login
                };
                authlibJarPath = await _authlibInjectorSetup.EnsureAgentJarAsync();
            }

            var opts = new LaunchOptions
            {
                Version = version, Mc = mc, Account = account, Java = java,
                MinMemoryMb = inst.MinMemoryMb, MaxMemoryMb = inst.MaxMemoryMb,
                WindowWidth = inst.WindowWidth, WindowHeight = inst.WindowHeight,
                AuthlibInjectorServer = authlibServer,
                AuthlibInjectorJarPath = authlibJarPath,
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

    /// <summary>Export the selected instance to a .zip bundle (instance.json + mods + config).</summary>
    [RelayCommand]
    private void ExportInstance(Instance instance)
    {
        if (_instanceTransfer is null || instance is null) return;
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string zipPath = Path.Combine(desktop, $"{instance.Name}-export.zip");
            _instanceTransfer.Export(instance, zipPath);
            Status = $"home.exported,{zipPath}";
        }
        catch (Exception ex) { Status = $"home.launch_failed,{ex.Message}"; _logger.LogError(ex, "Export failed."); }
    }

    /// <summary>Import an instance from a .zip bundle.</summary>
    [RelayCommand]
    private void ImportInstance(string zipPath)
    {
        if (_instanceTransfer is null || string.IsNullOrEmpty(zipPath)) return;
        try
        {
            Instance imported = _instanceTransfer.Import(zipPath);
            Instances.Add(imported);
            SelectedInstance = imported;
            Status = $"home.installed,{imported.Name}";
        }
        catch (Exception ex) { Status = $"home.launch_failed,{ex.Message}"; _logger.LogError(ex, "Import failed."); }
    }

    /// <summary>Clone the selected instance (copy config + game dir to a new name).</summary>
    [RelayCommand]
    private void CloneInstance(Instance instance)
    {
        if (instance is null) return;
        try
        {
            Instance clone = _instances.Clone(instance, $"{instance.Name} (copy)");
            Instances.Add(clone);
            SelectedInstance = clone;
            Status = $"home.installed,{clone.Name}";
        }
        catch (Exception ex) { Status = $"home.launch_failed,{ex.Message}"; _logger.LogError(ex, "Clone failed."); }
    }
}
