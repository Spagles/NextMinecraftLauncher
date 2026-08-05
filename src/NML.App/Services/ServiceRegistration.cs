using Microsoft.Extensions.DependencyInjection;
using NML.AICore;
using NML.AICore.Features;
using NML.AICore.LocalModels;
using NML.AICore.Secrets;
using NML.AICore.Tools;
using NML.App.Services;
using NML.Core;
using NML.Core.Auth;
using NML.Core.Auth.AuthlibInjector;
using NML.Core.Auth.Microsoft;
using NML.Core.Download;
using NML.Core.Instances;
using NML.Core.Java;
using NML.Core.Launch;
using NML.Core.Modloaders;
using NML.Core.Modpacks;
using NML.Core.Skins;
using NML.Core.Update;

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
        services.AddSingleton<ForgeInstaller>();
        services.AddSingleton<NeoForgeInstaller>();
        services.AddSingleton<OptiFineInstaller>();

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
        services.AddSingleton<AuthlibInjectorSetup>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            return new AuthlibInjectorSetup(
                sp.GetRequiredService<IHttpFetcher>(),
                Path.Combine(settings.SettingsDir, "authlib-injector"),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthlibInjectorSetup>.Instance);
        });

        // --- Settings & secrets ---
        services.AddSingleton(_ => new SettingsStore(settingsDir));
        services.AddSingleton<AccountStore>(_ => new AccountStore(settingsDir));
        services.AddSingleton<AuthlibInjectorServerStore>(_ => new AuthlibInjectorServerStore(settingsDir));
        services.AddSingleton<AuthlibInjectorProvider>();
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
        services.AddSingleton<InstanceTransferService>();

        // --- Game content browser ---
        services.AddSingleton<GameContentBrowser>();

        // --- Auto-update checker ---
        services.AddSingleton<UpdateChecker>(_ =>
            new UpdateChecker("weige0831", "NextMinecraftLauncher",
                (url, ct) => _.GetRequiredService<IHttpFetcher>().GetStringAsync(url, ct)));

        // --- Skin rendering ---
        services.AddSingleton<SkinService>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            var fetcher = sp.GetRequiredService<IHttpFetcher>();
            return new SkinService(fetcher, System.IO.Path.Combine(settings.SettingsDir, "skins"));
        });
        services.AddSingleton<SkinUploadService>();
        services.AddSingleton<ICommunitySkinSource>(sp =>
            new MineSkinSource(sp.GetRequiredService<IHttpFetcher>()));

        // --- Mod catalogs + recommender ---
        services.AddSingleton<NML.Data.Modrinth.ModrinthCatalog>();
        services.AddSingleton<NML.Data.IModCatalog>(sp => sp.GetRequiredService<NML.Data.Modrinth.ModrinthCatalog>());
        services.AddTransient<ModRecommenderFactory>();

        // --- Modpacks ---
        // ModpackInstaller optionally takes a CurseForge resolver; it's wired only when the
        // user has configured a CurseForge API key (resolved lazily at install time).
        services.AddSingleton<ModpackInstaller>(sp =>
        {
            var fetcher = sp.GetRequiredService<IHttpFetcher>();
            var downloader = sp.GetRequiredService<Downloader>();
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ModpackInstaller>.Instance;
            // CurseForge resolver is resolved lazily (key may be set/unset after DI build).
            return new ModpackInstaller(fetcher, downloader, logger, curseForgeResolver: null);
        });

        // --- Page view models (one singleton each; reused across navigations) ---
        services.AddSingleton<ViewModels.Pages.HomePageViewModel>();
        services.AddSingleton<ViewModels.Pages.DownloadPageViewModel>();
        services.AddSingleton<ViewModels.Pages.AccountsPageViewModel>();
        services.AddSingleton<ViewModels.Pages.ModsPageViewModel>();
        services.AddSingleton<ViewModels.Pages.AssistantPageViewModel>();
        services.AddSingleton<ViewModels.Pages.GameContentPageViewModel>();
        services.AddSingleton<ViewModels.Pages.SettingsPageViewModel>();

        return services;
    }
}

/// <summary>
/// Builds a <see cref="ModRecommender"/> from the currently-active AI provider, or null if
/// none is configured. Resolves the active provider at call time so the UI can switch providers.
/// </summary>
public sealed class ModRecommenderFactory
{
    private readonly SettingsStore _settings;
    private readonly ChatClientFactory _clients;

    public ModRecommenderFactory(SettingsStore settings, ChatClientFactory clients)
    {
        _settings = settings;
        _clients = clients;
    }

    public ModRecommender? TryCreate()
    {
        LauncherSettings s = _settings.Load();
        if (string.IsNullOrEmpty(s.ActiveProviderName)) return null;
        ChatProviderConfig? cfg = s.Providers.FirstOrDefault(p => p.Name == s.ActiveProviderName);
        return cfg is null ? null : new ModRecommender(_clients.Create(cfg),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ModRecommender>.Instance);
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
