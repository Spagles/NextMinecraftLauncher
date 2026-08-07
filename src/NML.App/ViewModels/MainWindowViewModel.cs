using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.App.ViewModels.Pages;
using NML.Core.Update;

namespace NML.App.ViewModels;

/// <summary>
/// The application shell's view model: owns the navigation list and the currently-active
/// page. Sidebar buttons call <see cref="NavigateToAsync"/> with a <see cref="PageViewModelBase"/>;
/// the content area binds to <see cref="CurrentPage"/>.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly UpdateChecker? _updateChecker;

    /// <summary>All navigable pages, in sidebar order.</summary>
    public ObservableCollection<PageViewModelBase> Pages { get; } = new();

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    /// <summary>Custom background image path (PCL-style). Bound to an Image layer behind the content.</summary>
    [ObservableProperty]
    private string? _backgroundImagePath;

    /// <summary>True when a background image is set (drives the Image layer visibility).</summary>
    public bool HasBackground => !string.IsNullOrEmpty(BackgroundImagePath);

    partial void OnBackgroundImagePathChanged(string? value) => OnPropertyChanged(nameof(HasBackground));

    /// <summary>True when the current UI language is RTL (Arabic, Hebrew, etc.).</summary>
    public bool IsRtl =>
        System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>FlowDirection for the window (RightToLeft for RTL languages, LeftToRight otherwise).</summary>
    public Avalonia.Media.FlowDirection WindowFlowDirection => IsRtl
        ? Avalonia.Media.FlowDirection.RightToLeft
        : Avalonia.Media.FlowDirection.LeftToRight;

    /// <summary>Re-raise RTL properties when the language changes.</summary>
    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(IsRtl));
        OnPropertyChanged(nameof(WindowFlowDirection));
    }

    /// <summary>Localized window title (resolves via {Loc}).</summary>
    public string Title => "Next Minecraft Launcher";

    /// <summary>The latest release detected by the startup update check, or null when up-to-date /
    /// check disabled / check failed. Drives the sidebar "new version" badge.</summary>
    [ObservableProperty]
    private UpdateInfo? _availableUpdate;

    /// <summary>True when <see cref="AvailableUpdate"/> is a genuinely newer release (badge visibility).</summary>
    public bool HasUpdateBanner => AvailableUpdate is { IsNewer: true };

    partial void OnAvailableUpdateChanged(UpdateInfo? value) => OnPropertyChanged(nameof(HasUpdateBanner));

    public MainWindowViewModel(
        Pages.HomePageViewModel home,
        Pages.DownloadPageViewModel download,
        Pages.AccountsPageViewModel accounts,
        Pages.MultiplayerPageViewModel multiplayer,
        Pages.ModsPageViewModel mods,
        Pages.AssistantPageViewModel assistant,
        Pages.GameContentPageViewModel content,
        Pages.SettingsPageViewModel settings,
        Services.SettingsStore settingsStore,
        UpdateChecker? updateChecker,
        ILogger<MainWindowViewModel> logger)
    {
        _logger = logger;
        _updateChecker = updateChecker;
        Pages.Add(home);
        Pages.Add(download);
        Pages.Add(accounts);
        Pages.Add(multiplayer);
        Pages.Add(mods);
        Pages.Add(assistant);
        Pages.Add(content);
        Pages.Add(settings);
        // Load the saved background image path.
        BackgroundImagePath = settingsStore.Load().BackgroundImagePath;
        // Subscribe to language changes for RTL re-evaluation.
        Localization.LocalizationService.Instance.LanguageChanged += (_, _) => NotifyLanguageChanged();
        NavigateTo(home);

        // Fire-and-forget a non-blocking startup update check when the user opted in (default on).
        // Failures are swallowed so a flaky network never affects startup.
        if (settingsStore.Load().CheckForUpdatesOnStartup != false && updateChecker is not null)
        {
            _ = CheckForUpdateOnStartupAsync();
        }
    }

    /// <summary>
    /// Background startup check against GitHub Releases. Sets <see cref="AvailableUpdate"/> only when
    /// a strictly newer release is found; otherwise leaves it null (no banner). Swallows all errors.
    /// </summary>
    private async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            string currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
            UpdateInfo? info = await _updateChecker!.CheckAsync(currentVersion);
            if (info is { IsNewer: true })
                AvailableUpdate = info;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Startup update check failed (non-fatal).");
        }
    }

    /// <summary>Switch the active page and trigger its lazy-load hook.</summary>
    [RelayCommand]
    private async Task NavigateToAsync(PageViewModelBase page)
    {
        if (page is null) return;
        CurrentPage = page;
        try { await page.OnNavigatedToAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Page navigation error."); }
    }

    /// <summary>Synchronous convenience for code-behind.</summary>
    public void NavigateTo(PageViewModelBase page) => NavigateToCommand.Execute(page);
}
