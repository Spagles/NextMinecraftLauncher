using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NML.App.ViewModels.Pages;

namespace NML.App.Views.Pages;

/// <summary>
/// Multiplayer page view: a saved-server roster with live Server-List-Ping status. Row
/// click selects the entry (mirrors the sidebar selection pattern); per-row actions live
/// in the selected-row action panel rather than per-item buttons to keep the list clean.
/// </summary>
public partial class MultiplayerPage : UserControl
{
    public MultiplayerPage() { InitializeComponent(); }

    /// <summary>Click a server row to select it (drives the action panel below the list).</summary>
    private void Row_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is ServerRow row && DataContext is MultiplayerPageViewModel vm)
            vm.SelectedRow = row;
    }
}
