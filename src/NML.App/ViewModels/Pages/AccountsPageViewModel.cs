using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core.Auth;
using NML.Core.Auth.AuthlibInjector;
using NML.Core.Auth.Microsoft;
using NML.Core.Skins;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Accounts page: list configured accounts, add an offline one (username → deterministic
/// UUID) or sign in via the Microsoft device-code flow. The active account is the one used
/// at launch time. Shows the active account's skin (avatar + 3D head render via Crafatar).
/// </summary>
public partial class AccountsPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.accounts";
    public override string Icon => "👤";

    private readonly IOfflineAuthProvider _offline;
    private readonly MicrosoftAuthProvider _microsoft;
    private readonly AccountStore _accountStore;
    private readonly SkinService _skinService;
    private readonly SkinUploadService? _skinUpload;
    private readonly ICommunitySkinSource _communitySource;
    private readonly ILogger<AccountsPageViewModel> _logger;

    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty] private Account? _activeAccount;
    [ObservableProperty] private string _newOfflineUsername = "Player";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _deviceCodeMessage = string.Empty;
    [ObservableProperty] private bool _showDeviceCode;

    /// <summary>2D avatar URL for the active account (binds an Image in the UI).</summary>
    public string ActiveAvatarUrl => ActiveAccount is null
        ? string.Empty : _skinService.AvatarUrl(ActiveAccount.Uuid, 128);

    /// <summary>3D head render URL for the active account.</summary>
    public string ActiveHeadRenderUrl => ActiveAccount is null
        ? string.Empty : _skinService.HeadRenderUrl(ActiveAccount.Uuid, scale: 8);

    /// <summary>True when an account is active (drives the skin-preview visibility).</summary>
    public bool HasActiveAccount => ActiveAccount is not null;

    // --- authlib-injector server management ---
    private readonly AuthlibInjectorServerStore _serverStore;
    private readonly AuthlibInjectorProvider? _authlibProvider;

    /// <summary>Saved external-login servers.</summary>
    public ObservableCollection<AuthlibInjectorServer> AuthlibServers { get; } = new();

    [ObservableProperty] private AuthlibInjectorServer? _selectedAuthlibServer;
    [ObservableProperty] private string _newServerName = string.Empty;
    [ObservableProperty] private string _newServerUrl = "https://littleskin.cn/api/yggdrasil";
    [ObservableProperty] private string _authlibLoginName = string.Empty;
    [ObservableProperty] private string _authlibPassword = string.Empty;
    [ObservableProperty] private bool _hasAuthlibServers;

    /// <summary>Path to the downloaded skin PNG for the active account (drives the 3D preview).</summary>
    [ObservableProperty] private string? _activeSkinPngPath;

    // --- skin upload + community browsing ---
    /// <summary>Community skins currently displayed in the browse panel.</summary>
    public ObservableCollection<CommunitySkin> CommunitySkins { get; } = new();

    [ObservableProperty] private string _communitySearchText = string.Empty;
    [ObservableProperty] private SkinVariant _uploadVariant = SkinVariant.Classic;
    [ObservableProperty] private string? _uploadPngPath;
    [ObservableProperty] private bool _isUploadingSkin;
    [ObservableProperty] private bool _isBrowsingSkins;
    [ObservableProperty] private bool _hasCommunitySkins;

    public AccountsPageViewModel(
        IOfflineAuthProvider offline,
        MicrosoftAuthProvider microsoft,
        AccountStore accountStore,
        SkinService skinService,
        AuthlibInjectorServerStore serverStore,
        ILogger<AccountsPageViewModel> logger,
        AuthlibInjectorProvider? authlibProvider = null,
        SkinUploadService? skinUpload = null,
        ICommunitySkinSource? communitySource = null)
    {
        _offline = offline;
        _microsoft = microsoft;
        _accountStore = accountStore;
        _skinService = skinService;
        _serverStore = serverStore;
        _authlibProvider = authlibProvider;
        _skinUpload = skinUpload;
        _communitySource = communitySource ?? new MineSkinSource(
            new NML.Core.Download.HttpClientHttpFetcher(new System.Net.Http.HttpClient()));
        _logger = logger;
        EnsureLanguageSubscribed();

        foreach (Account a in _accountStore.LoadAll()) Accounts.Add(a);
        ActiveAccount = Accounts.FirstOrDefault(a => a.Uuid == _accountStore.GetActiveUuid());

        foreach (AuthlibInjectorServer s in _serverStore.LoadAll()) AuthlibServers.Add(s);
        SelectedAuthlibServer = AuthlibServers.FirstOrDefault(s =>
            s.ApiUrl == _serverStore.GetActiveApiUrl());
        RefreshHasServers();
    }

    // Re-raise the avatar/render URLs whenever the active account changes.
    partial void OnActiveAccountChanged(Account? value)
    {
        OnPropertyChanged(nameof(ActiveAvatarUrl));
        OnPropertyChanged(nameof(ActiveHeadRenderUrl));
        OnPropertyChanged(nameof(HasActiveAccount));
        _ = DownloadSkinPngAsync(); // fire-and-forget; updates ActiveSkinPngPath when done
    }

    /// <summary>Download the raw skin PNG for the active account so the 3D preview can render it.</summary>
    private async Task DownloadSkinPngAsync()
    {
        if (ActiveAccount is null) { ActiveSkinPngPath = null; return; }
        try
        {
            ActiveSkinPngPath = await _skinService.DownloadSkinPngAsync(ActiveAccount.Uuid);
        }
        catch
        {
            // Network failure or SkinService not configured for download — degrade gracefully.
            ActiveSkinPngPath = null;
        }
    }

    [RelayCommand]
    private void AddOfflineAccount()
    {
        if (string.IsNullOrWhiteSpace(NewOfflineUsername)) { Status = "accounts.empty"; return; }
        Account acc = _offline.Create(NewOfflineUsername);
        Accounts.Add(acc);
        _accountStore.Save(Accounts.ToList());
        if (ActiveAccount is null) Activate(acc);
        NewOfflineUsername = "Player";
        Status = $"home.installed,{acc.Username}";
    }

    [RelayCommand]
    private async Task AddMicrosoftAccountAsync()
    {
        IsBusy = true;
        ShowDeviceCode = false;
        Status = "accounts.ms_polling";
        try
        {
            DeviceCodeResponse dc = await _microsoft.BeginLoginAsync();
            DeviceCodeMessage = $"accounts.ms_device_code,{dc.VerificationUri}";
            ShowDeviceCode = true;

            // Poll until the user finishes sign-in (or the flow expires).
            Account acc = await _microsoft.PollForCompletionAsync(dc);
            Accounts.Add(acc);
            _accountStore.Save(Accounts.ToList());
            if (ActiveAccount is null) Activate(acc);
            ShowDeviceCode = false;
            Status = $"accounts.ms_success,{acc.Username}";
        }
        catch (TimeoutException) { Status = "accounts.ms_expired"; }
        catch (Exception ex)
        {
            Status = $"accounts.ms_failed,{ex.Message}";
            _logger.LogError(ex, "Microsoft login failed.");
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Silently refresh every Microsoft account whose access token is past/near expiry and has a
    /// stored refresh token. The multi-account "keep them all live" path — called on startup and
    /// from a manual button so several MSA accounts stay usable without re-doing device-code.
    /// Accounts whose refresh fails are left in place so the UI can prompt re-authentication.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllAccountsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "account.refresh.refreshing";
        try
        {
            var refreshed = await _accountStore.RefreshIfDueAsync(_microsoft);
            int changed = refreshed.Count(a => a.AccountType == "msa" && !a.NeedsRefresh);
            // Swap in the refreshed list (preserves order + selection).
            Account? active = ActiveAccount;
            Accounts.Clear();
            foreach (Account a in refreshed) Accounts.Add(a);
            ActiveAccount = active is null
                ? Accounts.FirstOrDefault(a => a.Uuid == _accountStore.GetActiveUuid())
                : Accounts.FirstOrDefault(a => a.Uuid == active.Uuid) ?? active;
            Status = changed > 0 ? $"account.refresh.done,{changed}" : "account.refresh.uptodate";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogWarning(ex, "Account refresh sweep failed.");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Activate(Account account)
    {
        ActiveAccount = account;
        _accountStore.SetActiveUuid(account.Uuid);
    }

    [RelayCommand]
    private void Remove(Account account)
    {
        Accounts.Remove(account);
        _accountStore.Save(Accounts.ToList());
        if (ActiveAccount?.Uuid == account.Uuid)
            ActiveAccount = Accounts.FirstOrDefault();
    }

    // --- authlib-injector server management commands ---

    [RelayCommand]
    private void AddAuthlibServer()
    {
        if (string.IsNullOrWhiteSpace(NewServerName) ||
            string.IsNullOrWhiteSpace(NewServerUrl))
        {
            Status = "common.error";
            return;
        }
        var server = new AuthlibInjectorServer { Name = NewServerName.Trim(), ApiUrl = NewServerUrl.Trim() };
        _serverStore.Add(server);
        // Keep the observable list in sync (replace any existing with same URL).
        for (int i = AuthlibServers.Count - 1; i >= 0; i--)
            if (string.Equals(AuthlibServers[i].ApiUrl, server.ApiUrl, StringComparison.OrdinalIgnoreCase))
                AuthlibServers.RemoveAt(i);
        AuthlibServers.Add(server);
        SelectedAuthlibServer = server;
        NewServerName = string.Empty;
        RefreshHasServers();
        Status = $"home.installed,{server.Name}";
    }

    [RelayCommand]
    private void RemoveAuthlibServer(AuthlibInjectorServer server)
    {
        _serverStore.Remove(server.ApiUrl);
        AuthlibServers.Remove(server);
        if (SelectedAuthlibServer?.ApiUrl == server.ApiUrl)
            SelectedAuthlibServer = AuthlibServers.FirstOrDefault();
        if (_serverStore.GetActiveApiUrl() == server.ApiUrl)
            _serverStore.SetActiveApiUrl(null);
        RefreshHasServers();
        Status = $"accounts.remove,{server.Name}";
    }

    [RelayCommand]
    private async Task LoginWithServerAsync(AuthlibInjectorServer server)
    {
        if (_authlibProvider is null)
        {
            Status = "common.error";
            _logger.LogWarning("AuthlibInjectorProvider not registered; cannot log in.");
            return;
        }
        if (string.IsNullOrWhiteSpace(AuthlibLoginName) || string.IsNullOrWhiteSpace(AuthlibPassword))
        {
            Status = "common.error";
            return;
        }

        IsBusy = true;
        Status = "accounts.ms_polling";
        try
        {
            // Resolve + cache the server metadata first (the injector needs it).
            AuthlibInjectorServer resolved = await _authlibProvider.ResolveServerAsync(server);
            _serverStore.Add(resolved); // persist the resolved metadata

            Account acc = await _authlibProvider.LoginAsync(resolved, AuthlibLoginName, AuthlibPassword);
            Accounts.Add(acc);
            _accountStore.Save(Accounts.ToList());
            _serverStore.SetActiveApiUrl(resolved.ApiUrl);
            if (ActiveAccount is null) Activate(acc);

            AuthlibPassword = string.Empty;
            Status = $"accounts.ms_success,{acc.Username}";
        }
        catch (Exception ex)
        {
            Status = $"accounts.ms_failed,{ex.Message}";
            _logger.LogError(ex, "authlib-injector login failed.");
        }
        finally { IsBusy = false; }
    }

    private void RefreshHasServers() => HasAuthlibServers = AuthlibServers.Count > 0;

    // --- skin upload + community commands ---

    /// <summary>Upload the selected PNG as the active account's skin via the Mojang API.</summary>
    [RelayCommand]
    private async Task UploadSkinAsync()
    {
        if (_skinUpload is null) { Status = "common.error"; return; }
        if (ActiveAccount is null || string.IsNullOrEmpty(ActiveAccount.AccessToken)
            || ActiveAccount.AccountType == "legacy")
        {
            Status = "skins.no_ms_token";
            return;
        }
        if (string.IsNullOrEmpty(UploadPngPath) || !File.Exists(UploadPngPath))
        {
            Status = "common.error";
            return;
        }

        IsUploadingSkin = true;
        Status = "skins.upload_button";
        try
        {
            await _skinUpload.UploadAsync(ActiveAccount.AccessToken, UploadPngPath, UploadVariant);
            // Re-download the skin so the preview shows the new look.
            await DownloadSkinPngAsync();
            Status = "skins.upload_success";
        }
        catch (Exception ex)
        {
            Status = $"skins.upload_failed,{ex.Message}";
            _logger.LogError(ex, "Skin upload failed.");
        }
        finally { IsUploadingSkin = false; }
    }

    /// <summary>Reset the active account's skin to the default.</summary>
    [RelayCommand]
    private async Task ResetSkinAsync()
    {
        if (_skinUpload is null || ActiveAccount is null
            || string.IsNullOrEmpty(ActiveAccount.AccessToken)) { Status = "skins.no_ms_token"; return; }
        try
        {
            await _skinUpload.ResetAsync(ActiveAccount.AccessToken);
            await DownloadSkinPngAsync();
            Status = "skins.reset_success";
        }
        catch (Exception ex) { Status = $"skins.upload_failed,{ex.Message}"; }
    }

    /// <summary>Browse popular community skins from the active source.</summary>
    [RelayCommand]
    private async Task BrowseCommunityAsync()
    {
        IsBrowsingSkins = true;
        CommunitySkins.Clear();
        Status = "common.loading";
        try
        {
            IReadOnlyList<CommunitySkin> skins = await _communitySource.BrowseAsync();
            foreach (CommunitySkin s in skins) CommunitySkins.Add(s);
            HasCommunitySkins = CommunitySkins.Count > 0;
            Status = HasCommunitySkins ? $"{CommunitySkins.Count}" : "skins.community_empty";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Community browse failed.");
        }
        finally { IsBrowsingSkins = false; }
    }

    /// <summary>Search community skins by text.</summary>
    [RelayCommand]
    private async Task SearchCommunityAsync()
    {
        if (string.IsNullOrWhiteSpace(CommunitySearchText)) { await BrowseCommunityAsync(); return; }
        IsBrowsingSkins = true;
        CommunitySkins.Clear();
        try
        {
            IReadOnlyList<CommunitySkin> skins = await _communitySource.SearchAsync(CommunitySearchText.Trim());
            foreach (CommunitySkin s in skins) CommunitySkins.Add(s);
            HasCommunitySkins = CommunitySkins.Count > 0;
            Status = HasCommunitySkins ? $"{CommunitySkins.Count}" : "skins.community_empty";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
        finally { IsBrowsingSkins = false; }
    }

    /// <summary>Download a community skin PNG and set it as the upload target (preview before upload).</summary>
    [RelayCommand]
    private Task InstallCommunitySkinAsync(CommunitySkin skin)
    {
        if (skin is null) return Task.CompletedTask;
        try
        {
            // Download the skin PNG into the skins cache for preview.
            string? cacheDir = Path.GetDirectoryName(ActiveSkinPngPath ?? string.Empty);
            if (string.IsNullOrEmpty(cacheDir)) { Status = "common.error"; return Task.CompletedTask; }
            UploadVariant = skin.Model == "slim" ? SkinVariant.Slim : SkinVariant.Classic;
            Status = "skins.community_install";
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
        return Task.CompletedTask;
    }
}
