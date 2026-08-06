using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NML.App.ViewModels;
using NML.App.Views;

namespace NML.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    // Parameterless constructor keeps the Avalonia XAML designer happy.
    public App() => _services = null!;

    public App(IServiceProvider services) => _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build the main window immediately (synchronously) so the app has a window
            // and won't shut down. The splash shows on top as an overlay if it works.
            ShowMainWindow(desktop);

            // Optionally show a splash on top (non-blocking, non-fatal if it fails).
            _ = Task.Run(async () =>
            {
                try
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        var splash = new SplashScreenWindow();
                        splash.Show(desktop.MainWindow!);
                        await Task.Delay(2000);
                        splash.Close();
                    });
                }
                catch { /* splash is cosmetic; ignore all errors */ }
            });
        }

        _services.GetRequiredService<ILogger<App>>()
            .LogInformation("NextMinecraftLauncher started.");

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetService<MainWindowViewModel>(),
            };
            desktop.MainWindow.Show();
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<App>>()
                .LogError(ex, "Failed to create main window.");
        }
    }
}
