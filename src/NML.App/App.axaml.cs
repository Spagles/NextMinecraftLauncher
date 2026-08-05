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

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Show splash screen first (PCL-style boot animation).
            var splash = new SplashScreenWindow();
            splash.Show();

            // Build the main window while the splash is visible.
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetService<MainWindowViewModel>(),
            };

            // Play the splash fade sequence, then show the main window.
            await splash.PlayAsync();

            desktop.MainWindow.Show();
        }

        _services.GetRequiredService<ILogger<App>>()
            .LogInformation("NextMinecraftLauncher started.");

        base.OnFrameworkInitializationCompleted();
    }
}
