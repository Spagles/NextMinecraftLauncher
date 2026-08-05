using Avalonia.Controls;
using Avalonia.Interactivity;
using NML.App.ViewModels.Pages;

namespace NML.App.Views.Pages;

public partial class DownloadPage : UserControl
{
    public DownloadPage() { InitializeComponent(); }

    /// <summary>Filter-button click: set the VM's TypeFilter from the button's Tag.</summary>
    private void SelectFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string filter && DataContext is DownloadPageViewModel vm)
            vm.TypeFilter = filter;
    }
}
