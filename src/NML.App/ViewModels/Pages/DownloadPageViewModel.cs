using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.App.Services;
using NML.Core;
using NML.Core.Download;
using NML.Core.Instances;
using NML.Core.Models;

namespace NML.App.ViewModels.Pages;

/// <summary>
/// Download-center page: lists the full Mojang version manifest, with search and type
/// filtering (release/snapshot/old). Installing a version creates an Instance and runs
/// the vanilla installer end-to-end.
/// </summary>
public partial class DownloadPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.download";
    public override string Icon => "⬇️";

    private readonly VersionManifestService _manifest;
    private readonly VanillaInstaller _vanillaInstaller;
    private readonly VersionInfoService _versions;
    private readonly InstanceStore _instances;
    private readonly ILogger<DownloadPageViewModel> _logger;

    private IReadOnlyList<VersionManifestEntry> _all = Array.Empty<VersionManifestEntry>();

    /// <summary>Currently displayed (filtered) versions.</summary>
    public ObservableCollection<VersionManifestEntry> FilteredVersions { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _typeFilter = "release"; // release|snapshot|old_beta|all
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _installingVersion = string.Empty;
    [ObservableProperty] private int _installProgress;

    public DownloadPageViewModel(
        VersionManifestService manifest,
        VanillaInstaller vanillaInstaller,
        VersionInfoService versions,
        InstanceStore instances,
        ILogger<DownloadPageViewModel> logger)
    {
        _manifest = manifest;
        _vanillaInstaller = vanillaInstaller;
        _versions = versions;
        _instances = instances;
        _logger = logger;
        EnsureLanguageSubscribed();
    }

    public override async Task OnNavigatedToAsync()
    {
        if (_all.Count == 0) await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Status = "download.loading";
        try
        {
            VersionManifest m = await _manifest.GetAsync();
            _all = m.Versions;
            ApplyFilter();
            Status = $"download.results,{_all.Count}";
        }
        catch (Exception ex)
        {
            // Distinguish network errors (show a friendly localized message) from other errors.
            if (ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException)
                Status = "download.network_error";
            else
                Status = $"download.load_failed,{ex.Message}";
            _logger.LogError(ex, "Version manifest load failed.");
        }
        finally { IsLoading = false; }
    }

    /// <summary>Re-apply the search/type filter to the cached full list.</summary>
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnTypeFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredVersions.Clear();
        IEnumerable<VersionManifestEntry> src = _all;

        if (TypeFilter != "all")
        {
            // "all" shows everything; otherwise filter by exact Mojang type.
            // (Releases = "release", Snapshots = "snapshot", Old = "old_beta" + "old_alpha".)
            src = TypeFilter switch
            {
                "old_beta" => src.Where(v => v.Type == "old_beta" || v.Type == "old_alpha"),
                _ => src.Where(v => v.Type == TypeFilter),
            };
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string q = SearchText.Trim();
            src = src.Where(v => v.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (VersionManifestEntry v in src.Take(200)) FilteredVersions.Add(v);
    }

    [RelayCommand]
    private async Task InstallAsync(VersionManifestEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Id)) return;
        string versionId = entry.Id;
        string name = $"{versionId} (vanilla)";
        var instance = new Instance { Name = name, VersionId = versionId, MaxMemoryMb = 4096 };
        var mc = new MinecraftDirectory(_instances.GameDirFor(name));

        InstallingVersion = versionId;
        InstallProgress = 0;
        try
        {
            await _vanillaInstaller.InstallAsync(versionId, mc, progress: (in DownloadProgress p, string f) =>
            {
                if (p.TotalFiles > 0) InstallProgress = (int)(p.FileFraction * 100);
            });
            _instances.Add(instance);
            Status = $"home.installed,{versionId}";
        }
        catch (Exception ex)
        {
            Status = $"home.install_failed,{ex.Message}";
            _logger.LogError(ex, "Install of {Id} failed.", versionId);
        }
        finally { InstallingVersion = string.Empty; }
    }
}
