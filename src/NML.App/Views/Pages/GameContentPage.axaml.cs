using Avalonia.Controls;
using Avalonia.Interactivity;
using NML.App.ViewModels.Pages;

namespace NML.App.Views.Pages;

public partial class GameContentPage : UserControl
{
    public GameContentPage() { InitializeComponent(); }

    /// <summary>Tab-button click: set the VM's Tab property from the button's Tag.</summary>
    private void SelectTab(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tab && DataContext is GameContentPageViewModel vm)
            vm.Tab = tab;
    }
}
