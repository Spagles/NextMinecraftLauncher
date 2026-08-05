using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NML.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
