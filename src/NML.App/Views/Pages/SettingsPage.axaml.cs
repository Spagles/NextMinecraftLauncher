using Avalonia.Controls;
using Avalonia.Interactivity;
using NML.App.ViewModels.Pages;

namespace NML.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage() { InitializeComponent(); }

    /// <summary>Clear the background image path.</summary>
    private void ClearBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsPageViewModel vm)
        {
            vm.BackgroundImagePath = string.Empty;
        }
    }
}
