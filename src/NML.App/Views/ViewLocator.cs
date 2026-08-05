using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NML.App.ViewModels;
using NML.App.ViewModels.Pages;

namespace NML.App.Views;

/// <summary>
/// Maps a page view model to its <see cref="UserControl"/> view. Used by the main window's
/// content area so navigating to a new page VM swaps in the matching view automatically
/// (standard Avalonia view-locator pattern).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public bool SupportsRecycling => false;

    public Control? Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "—" };

        string vmName = data.GetType().FullName!;
        // NML.App.ViewModels.Pages.HomePageViewModel → NML.App.Views.Pages.HomePage
        string viewName = vmName
            .Replace("ViewModels.Pages.", "Views.Pages.")
            .Replace("PageViewModel", "Page");

        var viewType = Type.GetType(viewName);
        if (viewType is null)
            return new TextBlock { Text = $"No view for {viewName}" };

        return (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is PageViewModelBase;
}
