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
            // Show splash screen first (PCL-style boot animation).
            var splash = new SplashScreenWindow();
            splash.Show();

            // Play the splash sequence. When done, build + show the main window.
            // MainWindow construction (DI + ViewLocator + page VM resolution) is deferred
            // until after the splash animation so it doesn't block the fade.
            splash.PlayAsync().ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = _services.GetService<MainWindowViewModel>(),
                    };
                    desktop.MainWindow.Show();
                    splash.Close();
                });
            });
        }

        _services.GetRequiredService<ILogger<App>>()
            .LogInformation("NextMinecraftLauncher started.");

        base.OnFrameworkInitializationCompleted();
    }
}
