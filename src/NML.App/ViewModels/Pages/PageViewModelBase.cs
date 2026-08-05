using CommunityToolkit.Mvvm.ComponentModel;

namespace NML.App.ViewModels;

/// <summary>
/// Base for every navigable page's view model. Each page exposes a localized title key
/// and an icon glyph; the navigation service uses these for the sidebar and header.
/// </summary>
public abstract class PageViewModelBase : ObservableObject
{
    /// <summary>Localization key for this page's title (e.g. <c>nav.home</c>).</summary>
    public abstract string TitleKey { get; }

    /// <summary>Icon glyph shown in the sidebar (emoji for simplicity).</summary>
    public virtual string Icon { get; }

    /// <summary>Called when the page becomes the active view. Override for lazy loading.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
