using AiPet.Search;
using System.IO;
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
    private bool _enableContentSearch;
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

    public bool EnableContentSearch
    {
        get => _enableContentSearch;
        set
        {
            if (_enableContentSearch == value) return;
            var previous = _enableContentSearch;
            try
            {
                if (_settings is not null)
                {
                    var settings = _settings.Load();
                    settings.Features.EnableContentSearch = value;
                    _settings.Save(settings);
                }
                _enableContentSearch = value;
                _search?.SetContentSearchEnabled(value);
                OnPC();
                OnPCFor(nameof(ContentSearchStatus));
                OnPCFor(nameof(SearchPrivacyNotice));
                RaiseContentSearchCommandStates();
                RestartSearch(immediate: true);
                if (value) _ = RebuildContentIndexAsync();
                else Status = "已停用文件正文搜索并清除本地正文索引。";
            }
            catch
            {
                _enableContentSearch = previous;
                _search?.SetContentSearchEnabled(previous);
                OnPC();
                OnPCFor(nameof(ContentSearchStatus));
                OnPCFor(nameof(SearchPrivacyNotice));
                Status = "正文搜索设置未保存，请重试。";
            }
        }
    }

    public string SearchPrivacyNotice => EnableContentSearch
        ? "已启用受控正文搜索：只读取授权文件夹内不超过 128 KiB 的 .txt 和 .md；其他文件仍只匹配名称和路径。"
        : "确认前只展示候选路径，不会枚举或读取其中的文件；当前搜索只匹配名称和路径元数据。";

    public string ContentSearchStatus
    {
        get
        {
            if (!EnableContentSearch) return "已关闭 · 本地正文索引为空";
            if (_search is null) return "等待搜索服务";
            var summary = _search.GetContentSummary();
            var running = false;
            lock (_rangeIndexGate) running = _rangeIndexTasks.Values.Any(task => !task.IsCompleted);
            var size = summary.BytesRead < 1024
                ? $"{summary.BytesRead:N0} B"
                : $"{summary.BytesRead / 1024d:N1} KiB";
            var prefix = running ? "正在更新" : "可用";
            return $"{prefix} · 已索引 {summary.Indexed:N0} 个，跳过 {summary.Skipped:N0} 个，读取 {size}";
        }
    }

    public ICommand RebuildContentIndexCommand { get; private set; } = null!;
    public ICommand DisableContentSearchCommand { get; private set; } = null!;

    private void InitializeContentSearchCommands()
    {
        RebuildContentIndexCommand = new RelayCommand(
            async _ => await RebuildContentIndexAsync(),
            _ => EnableContentSearch && _search is not null && HasRanges);
        DisableContentSearchCommand = new RelayCommand(
            _ => EnableContentSearch = false,
            _ => EnableContentSearch && _search is not null);
    }

    public async Task RebuildContentIndexAsync()
    {
        if (!EnableContentSearch || _search is null) return;
        var rangeIds = _search.ListRanges()
            .Where(range => Directory.Exists(range.Path))
            .Select(range => range.Id)
            .ToArray();
        if (rangeIds.Length == 0)
        {
            Status = "请先添加允许搜索的文件夹。";
            return;
        }
        Status = "正在重建受控正文索引…";
        OnPCFor(nameof(ContentSearchStatus));
        await Task.WhenAll(rangeIds.Select(StartIndexRangeAsync));
        var incomplete = rangeIds
            .Where(id => _search.GetContentSummary(id).LastIndexedAt is null)
            .ToArray();
        if (EnableContentSearch && incomplete.Length > 0)
            await Task.WhenAll(incomplete.Select(StartIndexRangeAsync));
        OnPCFor(nameof(ContentSearchStatus));
        RestartSearch(immediate: true);
        if (EnableContentSearch) Status = "正文索引已更新。";
    }

    private void RaiseContentSearchCommandStates()
    {
        (RebuildContentIndexCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DisableContentSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
    private void SaveSearchRankingPreferences()
    {
        if (_settings is null) return;
        try { var settings=_settings.Load(); settings.Search.QueryField=SelectedSearchField.Id; settings.Search.UseRecentHistory=_useRecentSearchHistory; _settings.Save(settings); }
        catch { Status="搜索偏好未保存，请重试。"; }
    }
}
