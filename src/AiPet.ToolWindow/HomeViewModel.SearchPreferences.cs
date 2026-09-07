using AiPet.Search;
using System.Windows.Input;

namespace AiPet.ToolWindow;

public sealed record SearchFieldOption(string Id,string DisplayName,SearchField Field);
public sealed partial class HomeViewModel
{
    private long _searchPageRevision;
    public IReadOnlyList<SearchFieldOption> SearchFields { get; } = new[] { new SearchFieldOption("name","名称",SearchField.Name),new SearchFieldOption("path","相对路径",SearchField.RelativePath) };
    private SearchFieldOption? _selectedSearchField;
    public SearchFieldOption SelectedSearchField
    {
        get=>_selectedSearchField??SearchFields[0];
        set { if (value is null || _selectedSearchField==value) return; _selectedSearchField=value; OnPC(); SaveSearchRankingPreferences(); RestartSearch(immediate:true); }
    }
    private bool _useRecentSearchHistory;
    public bool UseRecentSearchHistory => _useRecentSearchHistory;
    public string SearchHistoryLabel => _useRecentSearchHistory?"停用最近使用排序":"启用最近使用排序";
    public void ToggleSearchHistory()
    {
        _useRecentSearchHistory=!_useRecentSearchHistory;
        SaveSearchRankingPreferences(); OnPCFor(nameof(SearchHistoryLabel)); RestartSearch(immediate:true);
    }
    public ICommand ToggleResultPinCommand => new RelayCommand(parameter=>
    { if (parameter is SearchItem item && _search is not null) { _search.SetPinned(item,!item.IsPinned); RestartSearch(immediate:true); } });
    public ICommand ClearSearchHistoryCommand => new RelayCommand(_=>
    { _search?.ClearHistory(); RestartSearch(immediate:true); Status="已清除最近使用记录，固定结果保留。"; });
    private void SaveSearchRankingPreferences()
    {
        if (_settings is null) return;
        try { var settings=_settings.Load(); settings.Search.QueryField=SelectedSearchField.Id; settings.Search.UseRecentHistory=_useRecentSearchHistory; _settings.Save(settings); }
        catch { Status="搜索偏好未保存，请重试。"; }
    }
}
