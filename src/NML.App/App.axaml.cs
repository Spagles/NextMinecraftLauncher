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
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        _services.GetRequiredService<ILogger<App>>()
            .LogInformation("NextMinecraftLauncher started.");

        base.OnFrameworkInitializationCompleted();
    }
}
