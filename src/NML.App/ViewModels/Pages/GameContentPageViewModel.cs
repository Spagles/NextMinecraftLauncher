using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Instances;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Game-content browser page: tabs for saves / screenshots / resource packs / mods, each
/// backed by <see cref="GameContentBrowser"/>. Reads from the active instance's game dir.
/// </summary>
public partial class GameContentPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.game_content";
    public override string Icon => "📁";

    private readonly InstanceStore _instances;
    private readonly ILogger<GameContentPageViewModel> _logger;

    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty] private string _tab = "saves"; // saves|screenshots|resourcepacks|mods
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;

    public GameContentPageViewModel(InstanceStore instances, ILogger<GameContentPageViewModel> logger)
    {
        _instances = instances;
        _logger = logger;
    }

    public override Task OnNavigatedToAsync() { Refresh(); return Task.CompletedTask; }

    partial void OnTabChanged(string value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        // Use the first instance's game dir (or fall back to default .minecraft).
        Instance? inst = _instances.LoadAll().FirstOrDefault();
        string root = inst is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : _instances.GameDirFor(inst.Name);
        var browser = new GameContentBrowser(new MinecraftDirectory(root));

        Items.Clear();
        try
        {
            switch (Tab)
            {
                case "saves":
                    foreach (GameSave s in browser.ListSaves()) Items.Add(s);
                    break;
                case "screenshots":
                    foreach (GameFile f in browser.ListScreenshots()) Items.Add(f);
                    break;
                case "resourcepacks":
                    foreach (GameFile f in browser.ListResourcePacks()) Items.Add(f);
                    break;
                case "mods":
                    foreach (GameFile f in browser.ListMods()) Items.Add(f);
                    break;
            }
            IsEmpty = Items.Count == 0;
            Status = IsEmpty ? "content.empty" : $"{Items.Count}";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Content load failed.");
        }
    }

    [RelayCommand]
    private void ToggleMod(GameFile file)
    {
        try
        {
            Instance? inst = _instances.LoadAll().FirstOrDefault();
            if (inst is null) return;
            var browser = new GameContentBrowser(new MinecraftDirectory(_instances.GameDirFor(inst.Name)));
            browser.ToggleMod(file.Path);
            Refresh();
        }
        catch (Exception ex) { Status = $"common.error,{ex.Message}"; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Instance? inst = _instances.LoadAll().FirstOrDefault();
        string root = inst is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : _instances.GameDirFor(inst.Name);
        string target = Tab == "saves" ? Path.Combine(root, "saves")
                      : Tab == "screenshots" ? Path.Combine(root, "screenshots")
                      : Tab == "resourcepacks" ? Path.Combine(root, "resourcepacks")
                      : Path.Combine(root, "mods");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { _logger.LogWarning(ex, "Open folder failed."); }
    }
}
