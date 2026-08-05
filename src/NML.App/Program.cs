using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NML.App.Localization;
using NML.App.Services;
using NML.App.ViewModels;

namespace NML.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize i18n before the UI builds so all {Loc} bindings resolve correctly.
        // The active culture is read from a saved settings file if present.
        string settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NextMinecraftLauncher");
        string? savedCulture = File.Exists(Path.Combine(settingsDir, "language.txt"))
            ? File.ReadAllText(Path.Combine(settingsDir, "language.txt")).Trim()
            : null;
        LocalizationBootstrapper.Initialize(savedCulture);

        using IHost host = BuildHost(args, settingsDir);
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

    private static IHost BuildHost(string[] args, string settingsDir) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                // Shell VM + page VMs (the page VMs are also registered by AddLauncherServices).
                services.AddSingleton<MainWindowViewModel>();

                // Engine + AI services
                services.AddLauncherServices(settingsDir);
            })
            .Build();

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure<App>(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
