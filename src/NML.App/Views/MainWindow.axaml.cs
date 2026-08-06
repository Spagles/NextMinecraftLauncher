using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using NML.App.ViewModels;

namespace NML.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        EnableMicaBackdrop();
    }

    /// <summary>Enable native Windows 11 Mica backdrop. Gracefully no-ops on older OS.</summary>
    private void EnableMicaBackdrop()
    {
        try
        {
            // Avalonia 11.3: set TransparencyLevelHint on TopLevel to request Mica.
            // On Win11 this enables the native Mica blur behind the window.
            // On older OS it silently falls back — the translucent brushes still look fine on solid bg.
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica };
        }
        catch
        {
            // Non-Win11 or unsupported — keep the default solid background.
        }
    }

    /// <summary>Drag the window by the custom title bar (PCL-style frameless window).</summary>
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// Multi-value converter bound to <c>Classes.active</c> on a nav-rail item. Receives
/// <c>[shell.CurrentPage, item]</c> and returns <c>true</c> when the item is the active
/// page, otherwise <c>false</c>. This is what drives the HMCL-style selected highlight
/// via the <c>Button.nav.active</c> style.
/// </summary>
public class NavActiveClassConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is { Count: >= 2 })
        {
            return ReferenceEquals(values[0], values[1]);
        }
        return false;
    }
}
