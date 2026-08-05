using CommunityToolkit.Mvvm.ComponentModel;
using NML.App.Localization;

namespace NML.App.ViewModels;

/// <summary>
/// Base for every navigable page's view model. Each page exposes a localized title key
/// and an icon glyph; the navigation service uses these for the sidebar and header.
/// <para>
/// <see cref="LocalizedTitle"/> resolves <see cref="TitleKey"/> through the
/// <see cref="LocalizationService"/> and refreshes when the active language changes, so
/// sidebar labels and the page header stay localized at runtime without re-navigating.
/// </para>
/// </summary>
public abstract class PageViewModelBase : ObservableObject
{
    /// <summary>Localization key for this page's title (e.g. <c>nav.home</c>).</summary>
    public abstract string TitleKey { get; }

    /// <summary>Icon glyph shown in the sidebar (emoji for simplicity).</summary>
    public virtual string Icon { get; } = "";

    /// <summary>
    /// The localized title, resolved from <see cref="TitleKey"/> against the current culture.
    /// Re-raised whenever the active language changes.
    /// </summary>
    public string LocalizedTitle => LocalizationService.Instance[TitleKey];

    private bool _subscribed;
    protected void EnsureLanguageSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(LocalizedTitle));
    }

    /// <summary>Called when the page becomes the active view. Override for lazy loading.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
