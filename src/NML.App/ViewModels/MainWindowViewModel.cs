using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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
