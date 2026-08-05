using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.AICore;
using NML.AICore.LocalModels;
using NML.App.Localization;
using NML.App.Services;
using NML.Core.Java;
using NML.Core.Update;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Settings page: language switcher (live-applies), Minecraft path, detected Java runtimes,
/// and AI-provider management (detect local models, add cloud providers, activate one).
/// </summary>
public partial class SettingsPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.settings";
    public override string Icon => "⚙️";

    private readonly SettingsStore _settings;
    private readonly LocalModelProbe _probe;
    private readonly ChatClientFactory _factory;
    private readonly JavaRuntimeDetector _javaDetector;
    private readonly ILogger<SettingsPageViewModel> _logger;

    public ObservableCollection<JavaRuntime> JavaRuntimes { get; } = new();
    public ObservableCollection<ChatProviderConfig> Providers { get; } = new();
    public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new();

    [ObservableProperty] private CultureInfo? _selectedLanguage;
    [ObservableProperty] private string _minecraftPath = string.Empty;
    [ObservableProperty] private string _newProviderName = string.Empty;
    [ObservableProperty] private string _newProviderUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string _newProviderModel = string.Empty;
    [ObservableProperty] private string _newProviderKey = string.Empty;
    [ObservableProperty] private ChatProviderConfig? _activeProvider;
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Active UI theme: "dark", "light", or "system".</summary>
    [ObservableProperty] private string _theme = "dark";

    /// <summary>Custom background image path (PCL-style). Empty = default.</summary>
    [ObservableProperty] private string _backgroundImagePath = string.Empty;

    /// <summary>Custom accent color hex (e.g. "#4fc3f7"). Empty = default blue.</summary>
    [ObservableProperty] private string _accentColor = "#4fc3f7";

    /// <summary>Preset accent colors for quick selection.</summary>
    public IReadOnlyList<string> AccentPresets { get; } = new[]
    {
        "#4fc3f7", // blue
        "#66bb6a", // green
        "#ff7043", // orange
        "#ab47bc", // purple
        "#ef5350", // red
        "#26c6da", // cyan
        "#ffa726", // amber
    };

    partial void OnAccentColorChanged(string value)
    {
        // Apply globally via Avalonia's Application.Current.Resources.
        try
        {
            if (string.IsNullOrEmpty(value)) return;
            var color = Avalonia.Media.Color.Parse(value);
            Avalonia.Application.Current!.Resources["SystemAccentColor"] = color;
        }
        catch { /* invalid hex — ignore */ }
        // Persist so the accent survives restarts.
        PersistSettings();
    }

    partial void OnBackgroundImagePathChanged(string value)
    {
        PersistSettings();
        // Sync to MainWindowVM so the background Image layer updates live (not just on restart).
        if (_mainWindowVm is not null)
            _mainWindowVm.BackgroundImagePath = string.IsNullOrEmpty(value) ? null : value;
    }

    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private string _updateUrl = string.Empty;
    [ObservableProperty] private bool _isCheckingUpdate;
    private readonly UpdateChecker? _updateChecker;

    /// <summary>Available theme choices for the dropdown.</summary>
    public IReadOnlyList<string> ThemeChoices { get; } = new[] { "dark", "light", "system" };

    partial void OnThemeChanged(string value)
    {
        // Apply the theme globally via Avalonia's RequestedThemeVariant.
        var variant = value switch
        {
            "light" => Avalonia.Styling.ThemeVariant.Light,
            "system" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };
        Avalonia.Application.Current!.RequestedThemeVariant = variant;
        // Persist the theme choice so it survives restarts.
        PersistSettings();
    }

    private readonly MainWindowViewModel? _mainWindowVm;

    public SettingsPageViewModel(
        SettingsStore settings,
        LocalModelProbe probe,
        ChatClientFactory factory,
        JavaRuntimeDetector javaDetector,
        ILogger<SettingsPageViewModel> logger,
        UpdateChecker? updateChecker = null,
        MainWindowViewModel? mainWindowVm = null)
    {
        _settings = settings;
        _probe = probe;
        _factory = factory;
        _javaDetector = javaDetector;
        _logger = logger;
        _updateChecker = updateChecker;
        _mainWindowVm = mainWindowVm;
        EnsureLanguageSubscribed();

        // Populate the language picker from the registered cultures.
        foreach (CultureInfo c in LocalizationService.Instance.AvailableCultures) AvailableLanguages.Add(c);

        LauncherSettings s = settings.Load();
        MinecraftPath = s.MinecraftRoot ?? string.Empty;
        BackgroundImagePath = s.BackgroundImagePath ?? string.Empty;
        AccentColor = s.AccentColor ?? "#4fc3f7";
        Theme = s.Theme ?? "dark";
        foreach (ChatProviderConfig p in s.Providers) Providers.Add(p);
        SelectedLanguage = AvailableLanguages.FirstOrDefault(c =>
            c.Name.Equals(LocalizationService.Instance.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
        ActiveProvider = Providers.FirstOrDefault(p => p.Name == s.ActiveProviderName);
    }

    /// <summary>Live-apply language when the user picks one.</summary>
    partial void OnSelectedLanguageChanged(CultureInfo? value)
    {
        if (value is not null)
        {
            LocalizationService.Instance.CurrentCulture = value;
            // Persist the culture to language.txt so Program.cs reads it on next startup.
            string langPath = Path.Combine(_settings.SettingsDir, "language.txt");
            File.WriteAllText(langPath, value.Name);
            // Also save the full settings.
            var s = _settings.Load();
            s.Providers = Providers.ToList();
            s.ActiveProviderName = ActiveProvider?.Name;
            _settings.Save(s);
        }
    }

    public override Task OnNavigatedToAsync()
    {
        if (JavaRuntimes.Count == 0) DetectJava();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void DetectJava()
    {
        JavaRuntimes.Clear();
        foreach (JavaRuntime j in _javaDetector.DetectAll()) JavaRuntimes.Add(j);
        Status = JavaRuntimes.Count > 0 ? $"{JavaRuntimes.Count}" : "common.error";
    }

    [RelayCommand]
    private async Task DetectLocalModelsAsync()
    {
        Status = "common.loading";
        IReadOnlyList<ChatProviderConfig> found = await _probe.DetectAsync();
        foreach (ChatProviderConfig p in found)
            if (!Providers.Any(x => x.BaseUrl == p.BaseUrl)) Providers.Add(p);
        Status = found.Count > 0 ? $"settings.local_detected,{found.Count}" : "settings.local_none";
    }

    [RelayCommand]
    private void AddCloudProvider()
    {
        if (string.IsNullOrWhiteSpace(NewProviderName) ||
            string.IsNullOrWhiteSpace(NewProviderModel) ||
            string.IsNullOrWhiteSpace(NewProviderKey))
        { Status = "common.error"; return; }

        var cfg = new ChatProviderConfig
        {
            Kind = ChatProviderKind.OpenAiCompatible,
            Name = NewProviderName, BaseUrl = NewProviderUrl,
            Model = NewProviderModel, ApiKey = NewProviderKey,
        };
        try { _factory.Create(cfg); }
        catch (ArgumentException ex) { Status = $"common.error,{ex.Message}"; return; }

        Providers.Add(cfg.With());
        PersistSettings();
        NewProviderName = NewProviderModel = NewProviderKey = string.Empty;
        Status = $"home.installed,{cfg.Name}";
    }

    [RelayCommand]
    private void Activate(ChatProviderConfig provider)
    {
        ActiveProvider = provider;
        PersistSettings();
        Status = $"settings.ai_active_provider,{provider.Name}";
    }

    private void PersistSettings()
    {
        var s = _settings.Load();
        s.Providers = Providers.ToList();
        s.ActiveProviderName = ActiveProvider?.Name;
        s.MinecraftRoot = MinecraftPath;
        s.BackgroundImagePath = string.IsNullOrEmpty(BackgroundImagePath) ? null : BackgroundImagePath;
        s.AccentColor = string.IsNullOrEmpty(AccentColor) ? null : AccentColor;
        s.Theme = Theme;
        _settings.Save(s);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_updateChecker is null) { UpdateStatus = "common.error"; return; }
        IsCheckingUpdate = true;
        UpdateStatus = "common.loading";
        try
        {
            // Read the actual running version from the assembly, not a hardcoded string.
            string currentVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "0.1.0";
            var info = await _updateChecker.CheckAsync(currentVersion);
            if (info is null || !info.IsNewer)
            {
                UpdateStatus = "update.up_to_date";
                UpdateUrl = string.Empty;
            }
            else
            {
                UpdateStatus = $"update.available,{info.TagName}";
                UpdateUrl = info.HtmlUrl;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Update check failed.");
        }
        finally { IsCheckingUpdate = false; }
    }
}

internal static class ProviderRecordCopy
{
    // `cfg with { }` only works on records; ChatProviderConfig is a class. Provide a copy helper.
    public static ChatProviderConfig With(this ChatProviderConfig p) => new()
    {
        Kind = p.Kind, Name = p.Name, BaseUrl = p.BaseUrl, Model = p.Model, ApiKey = p.ApiKey,
        Temperature = p.Temperature, MaxOutputTokens = p.MaxOutputTokens,
    };
}
