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
            vm.BackgroundImagePath = string.Empty;
    }

    /// <summary>Pick an accent color from a preset button (Tag = hex color).</summary>
    private void PickAccent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex && DataContext is SettingsPageViewModel vm)
            vm.AccentColor = hex;
    }
}
