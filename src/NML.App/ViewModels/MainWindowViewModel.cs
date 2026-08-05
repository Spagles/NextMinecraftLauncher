using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NML.App.ViewModels;

/// <summary>
/// Sample view model for the M0 skeleton. Demonstrates the CommunityToolkit.Mvvm
/// source-generator pattern (observable property + relay command) that the rest of
/// the launcher's ViewModels will follow in later milestones.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "NextMinecraftLauncher";

    [ObservableProperty]
    private string _status = "Ready — M0 skeleton running.";

    [ObservableProperty]
    private int _clickCount;

    [RelayCommand]
    private void Ping()
    {
        ClickCount++;
        Status = $"Pong! ({ClickCount})";
    }
}
