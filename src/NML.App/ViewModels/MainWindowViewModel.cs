using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.App.ViewModels.Pages;

namespace NML.App.ViewModels;

/// <summary>
/// The application shell's view model: owns the navigation list and the currently-active
/// page. Sidebar buttons call <see cref="NavigateToAsync"/> with a <see cref="PageViewModelBase"/>;
/// the content area binds to <see cref="CurrentPage"/>.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogger<MainWindowViewModel> _logger;

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

    /// <summary>True when the current language is RTL (Arabic, Hebrew, etc.).</summary>
    public bool IsRtl =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.IsRightToLeft;

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

    public MainWindowViewModel(
        Pages.HomePageViewModel home,
        Pages.DownloadPageViewModel download,
        Pages.AccountsPageViewModel accounts,
        Pages.ModsPageViewModel mods,
        Pages.AssistantPageViewModel assistant,
        Pages.GameContentPageViewModel content,
        Pages.SettingsPageViewModel settings,
        Services.SettingsStore settingsStore,
        ILogger<MainWindowViewModel> logger)
    {
        _logger = logger;
        Pages.Add(home);
        Pages.Add(download);
        Pages.Add(accounts);
        Pages.Add(mods);
        Pages.Add(assistant);
        Pages.Add(content);
        Pages.Add(settings);
        // Load the saved background image path.
        BackgroundImagePath = settingsStore.Load().BackgroundImagePath;
        // Subscribe to language changes for RTL re-evaluation.
        Localization.LocalizationService.Instance.LanguageChanged += (_, _) => NotifyLanguageChanged();
        NavigateTo(home);
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
