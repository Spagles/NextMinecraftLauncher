using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

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
