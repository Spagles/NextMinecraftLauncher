using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core.Auth;
using NML.Core.Auth.Microsoft;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Accounts page: list configured accounts, add an offline one (username → deterministic
/// UUID) or sign in via the Microsoft device-code flow. The active account is the one used
/// at launch time.
/// </summary>
public partial class AccountsPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.accounts";
    public override string Icon => "👤";

    private readonly IOfflineAuthProvider _offline;
    private readonly MicrosoftAuthProvider _microsoft;
    private readonly AccountStore _accountStore;
    private readonly ILogger<AccountsPageViewModel> _logger;

    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty] private Account? _activeAccount;
    [ObservableProperty] private string _newOfflineUsername = "Player";
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _deviceCodeMessage = string.Empty;
    [ObservableProperty] private bool _showDeviceCode;

    public AccountsPageViewModel(
        IOfflineAuthProvider offline,
        MicrosoftAuthProvider microsoft,
        AccountStore accountStore,
        ILogger<AccountsPageViewModel> logger)
    {
        _offline = offline;
        _microsoft = microsoft;
        _accountStore = accountStore;
        _logger = logger;

        foreach (Account a in _accountStore.LoadAll()) Accounts.Add(a);
        ActiveAccount = Accounts.FirstOrDefault(a => a.Uuid == _accountStore.GetActiveUuid());
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
}
