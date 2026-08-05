using Microsoft.Extensions.DependencyInjection;
using NML.AICore;
using NML.AICore.Features;
using NML.AICore.LocalModels;
using NML.AICore.Secrets;
using NML.AICore.Tools;
using NML.App.Services;
using NML.Core;
using NML.Core.Auth;
using NML.Core.Auth.Microsoft;
using NML.Core.Download;
using NML.Core.Instances;
using NML.Core.Java;
using NML.Core.Launch;
using NML.Core.Modloaders;

namespace NML.App.Services;

/// <summary>
/// Wires every launcher engine service (Core + AICore) into the DI container. Called once
/// from <c>Program.cs</c> at startup. Services are singletons where they hold no per-request
/// state (manifest services, installers) and transient where cheap to build.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddLauncherServices(this IServiceCollection services, string settingsDir)
    {
        // --- HTTP & download ---
        services.AddHttpClient();
        services.AddHttpClient("launcher", c =>
        {
            c.Timeout = TimeSpan.FromMinutes(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "NextMinecraftLauncher/0.1 (https://github.com/weige0831/NextMinecraftLauncher)");
        });
        services.AddSingleton<IHttpFetcher>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpClientHttpFetcher(factory.CreateClient("launcher"));
        });
        services.AddSingleton<Downloader>();

        // --- Mojang version services ---
        services.AddSingleton<VersionManifestService>();
        services.AddSingleton<VersionInfoService>();
        services.AddSingleton<VanillaInstaller>();

        // --- Modloaders ---
        services.AddSingleton<FabricInstaller>();
        services.AddSingleton<QuiltInstaller>();

        // --- Auth ---
        services.AddSingleton<IOfflineAuthProvider, OfflineAuthProvider>();
        services.AddSingleton<IMicrosoftExchange, HttpMicrosoftExchange>();
        services.AddSingleton<MicrosoftAuthProvider>();

        // --- Java ---
        services.AddSingleton<JavaRuntimeDetector>();
        services.AddSingleton<JavaRuntimeInstaller>();

        // --- Launch ---
        services.AddSingleton<LaunchCommandBuilder>();
        services.AddSingleton<ProcessLauncher>();

        // --- Settings & secrets ---
        services.AddSingleton(_ => new SettingsStore(settingsDir));
        services.AddSingleton<ISecretStore>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            return new DpapiSecretStore(Path.Combine(settings.SettingsDir, "secrets"));
        });

        // --- AI ---
        services.AddSingleton<LocalModelProbe>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new LocalModelProbe(factory.CreateClient("launcher"),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalModelProbe>.Instance);
        });
        services.AddSingleton<ChatClientFactory>();
        // CrashAnalyzer + NaturalConfigAgent are created on demand from the active provider,
        // so they're registered as factories rather than singletons bound to one client.
        services.AddTransient<CrashAnalyzerFactory>();
        services.AddTransient<NaturalConfigAgentFactory>();

        // --- Instance store ---
        services.AddSingleton<InstanceStore>(_ => new InstanceStore(settingsDir));

        // --- Game content browser ---
        services.AddSingleton<GameContentBrowser>();

        return services;
    }
}

/// <summary>
/// Builds a <see cref="CrashAnalyzer"/> from the currently-active AI provider. The active
/// provider is resolved at call time (not construction) so the UI can switch providers.
/// </summary>
public sealed class CrashAnalyzerFactory
{
    private readonly SettingsStore _settings;
    private readonly ChatClientFactory _clients;

    public CrashAnalyzerFactory(SettingsStore settings, ChatClientFactory clients)
    {
        _settings = settings;
        _clients = clients;
    }

    public CrashAnalyzer? TryCreate()
    {
        IChatClient? client = ResolveActiveClient();
        return client is null ? null : new CrashAnalyzer(client,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CrashAnalyzer>.Instance);
    }

    private IChatClient? ResolveActiveClient()
    {
        LauncherSettings s = _settings.Load();
        if (string.IsNullOrEmpty(s.ActiveProviderName)) return null;
        ChatProviderConfig? cfg = s.Providers.FirstOrDefault(p => p.Name == s.ActiveProviderName);
        return cfg is null ? null : _clients.Create(cfg);
    }
}

public sealed class NaturalConfigAgentFactory
{
    private readonly SettingsStore _settings;
    private readonly ChatClientFactory _clients;

    public NaturalConfigAgentFactory(SettingsStore settings, ChatClientFactory clients)
    {
        _settings = settings;
        _clients = clients;
    }

    public NaturalConfigAgent? TryCreate()
    {
        LauncherSettings s = _settings.Load();
        if (string.IsNullOrEmpty(s.ActiveProviderName)) return null;
        ChatProviderConfig? cfg = s.Providers.FirstOrDefault(p => p.Name == s.ActiveProviderName);
        return cfg is null ? null : new NaturalConfigAgent(_clients.Create(cfg),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NaturalConfigAgent>.Instance);
    }
}
