using AiPet.Search;
using System.Windows.Input;

namespace AiPet.ToolWindow;

public sealed partial class HomeViewModel
{
    private ISearchResultActions _searchResultActions = new WindowsSearchResultActions();
    private SearchItem? _selectedResult;

    public SearchItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (ReferenceEquals(_selectedResult, value)) return;
            _selectedResult = value;
            OnPC();
            OnPCFor(nameof(HasSelectedResult));
            OnPCFor(nameof(SelectedResultSummary));
            RaiseSearchResultCommandStates();
        }
    }

    public bool HasSelectedResult => SelectedResult is not null;

    public string SelectedResultSummary => SelectedResult is null
        ? string.Empty
        : $"已选：{SelectedResult.Name} · {KindLabel(SelectedResult.Kind)} · {FormatSize(SelectedResult)}";

    public ICommand OpenSelectedResultCommand { get; private set; } = null!;
    public ICommand RevealSelectedResultCommand { get; private set; } = null!;
    public ICommand CopySelectedResultPathCommand { get; private set; } = null!;
    public ICommand AddSelectedResultToShortcutsCommand { get; private set; } = null!;

    private void InitializeSearchResultCommands()
    {
        OpenSelectedResultCommand = new RelayCommand(
            _ => OpenSelectedResult(),
            _ => SelectedResult?.ExistsNow == true);
        RevealSelectedResultCommand = new RelayCommand(
            _ => RevealSelectedResult(),
            _ => SelectedResult?.ExistsNow == true);
        CopySelectedResultPathCommand = new RelayCommand(
            _ => CopySelectedResultPath(),
            _ => SelectedResult is not null);
        AddSelectedResultToShortcutsCommand = new RelayCommand(
            _ => AddSelectedResultToShortcuts(),
            _ => SelectedResult?.ExistsNow == true && _shortcuts is not null);
    }

    private void OpenSelectedResult()
    {
        var item = SelectedResult;
        if (!TryGetExistingSelectedResult(item)) return;
        try
        {
            _searchResultActions.Open(item!);
            if (UseRecentSearchHistory) _search?.RecordUse(item!);
            Status = $"已提交打开请求：{item!.Name}";
        }
        catch
        {
            Status = "打开失败，请检查权限或默认程序。";
        }
    }

    private void RevealSelectedResult()
    {
        var item = SelectedResult;
        if (!TryGetExistingSelectedResult(item)) return;
        try
        {
            _searchResultActions.Reveal(item!);
            Status = $"已在文件资源管理器中定位：{item!.Name}";
        }
        catch
        {
            Status = "定位失败，请检查文件资源管理器是否可用。";
        }
    }

    private void CopySelectedResultPath()
    {
        var item = SelectedResult;
        if (item is null) return;
        try
        {
            _searchResultActions.CopyPath(item);
            Status = "已复制所选目标路径。";
        }
        catch
        {
            Status = "复制路径失败，请检查剪贴板是否被占用。";
        }
    }

    private void AddSelectedResultToShortcuts()
    {
        var item = SelectedResult;
        if (!TryGetExistingSelectedResult(item)) return;
        AddShortcut(item!.FullPath);
    }

    private bool TryGetExistingSelectedResult(SearchItem? item)
    {
        if (item?.ExistsNow == true) return true;
        Status = "目标已不存在，请刷新或重建索引。";
        RaiseSearchResultCommandStates();
        return false;
    }

    private void ClearSelectedResult() => SelectedResult = null;

    private void RestoreSelectedResult(Guid? rangeId, string? fullPath)
    {
        SelectedResult = rangeId is null || string.IsNullOrWhiteSpace(fullPath)
            ? null
            : Results.FirstOrDefault(item =>
                item.RangeId == rangeId.Value
                && string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private void RaiseSearchResultCommandStates()
    {
        foreach (var command in new[]
                 {
                     OpenSelectedResultCommand,
                     RevealSelectedResultCommand,
                     CopySelectedResultPathCommand,
                     AddSelectedResultToShortcutsCommand,
                 })
            (command as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string KindLabel(SearchItemKind kind) => kind switch
    {
        SearchItemKind.Folder => "文件夹",
        SearchItemKind.Document => "文档",
        SearchItemKind.Application => "应用",
        SearchItemKind.Image => "图片",
        SearchItemKind.Video => "视频",
        SearchItemKind.Audio => "音频",
        _ => "文件",
    };

    private static string FormatSize(SearchItem item)
    {
        if (item.Kind == SearchItemKind.Folder) return "文件夹";
        if (item.SizeBytes < 1024) return $"{item.SizeBytes:N0} B";
        if (item.SizeBytes < 1024 * 1024) return $"{item.SizeBytes / 1024d:N1} KiB";
        if (item.SizeBytes < 1024L * 1024 * 1024) return $"{item.SizeBytes / (1024d * 1024):N1} MiB";
        return $"{item.SizeBytes / (1024d * 1024 * 1024):N1} GiB";
    }
}
