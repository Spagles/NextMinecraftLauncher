using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.App.ViewModels;

namespace NML.App;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't
    // initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using IHost host = BuildHost(args);
        host.Start();

        try
        {
            BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Builds the generic host with DI, logging, configuration, and every launcher service
    /// (engine + AI) registered via <see cref="ServiceRegistration"/>.
    /// </summary>
    private static IHost BuildHost(string[] args)
    {
        // Launcher settings live under %APPDATA%/NextMinecraftLauncher (per-user, survives updates).
        string settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NextMinecraftLauncher");

        return Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<AiSettingsViewModel>();
                services.AddSingleton<LauncherViewModel>();

                // Engine + AI services
                services.AddLauncherServices(settingsDir);
            })
            .Build();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure<App>(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
