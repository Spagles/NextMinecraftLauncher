using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NML.AICore.Features;
using NML.App.Services;
using NML.Data;
using NML.Data.Modrinth;

namespace NML.App.ViewModels.Pages;
public partial class ModsPageViewModel : PageViewModelBase
{
    public override string TitleKey => "nav.mods";
    public override string Icon => "🧩";

    private readonly ModrinthCatalog _catalog;
    private readonly ModRecommenderFactory _recommenderFactory;
    private readonly ILogger<ModsPageViewModel> _logger;

    public ObservableCollection<ModSearchResult> Results { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _recommendPrompt = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isRecommending;
    [ObservableProperty] private string _status = string.Empty;

    public ModsPageViewModel(
        ModrinthCatalog catalog,
        ModRecommenderFactory recommenderFactory,
        ILogger<ModsPageViewModel> logger)
    {
        _catalog = catalog;
        _recommenderFactory = recommenderFactory;
        _logger = logger;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        IsSearching = true;
        Results.Clear();
        Status = "common.loading";
        try
        {
            IReadOnlyList<ModSearchResult> r = await _catalog.SearchAsync(SearchText.Trim());
            foreach (ModSearchResult m in r) Results.Add(m);
            Status = r.Count > 0 ? $"mods.results,{r.Count}" : "mods.no_results";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "Mod search failed.");
        }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private async Task RecommendAsync()
    {
        ModRecommender? recommender = _recommenderFactory.TryCreate();
        if (recommender is null || string.IsNullOrWhiteSpace(RecommendPrompt))
        {
            Status = "assistant.no_provider";
            return;
        }
        IsRecommending = true;
        Results.Clear();
        Status = "assistant.thinking";
        try
        {
            IReadOnlyList<ModRecommendation> recs =
                await recommender.RecommendAsync(_catalog, RecommendPrompt.Trim());
            foreach (ModRecommendation r in recs) Results.Add(r.Mod);
            Status = recs.Count > 0 ? $"mods.results,{recs.Count}" : "mods.no_results";
        }
        catch (Exception ex)
        {
            Status = $"common.error,{ex.Message}";
            _logger.LogError(ex, "AI recommendation failed.");
        }
        finally { IsRecommending = false; }
    }
}
