using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AiPet.AI;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed class TodayPlanCandidateViewModel : INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public TodayPlanCandidateViewModel(TodoItem item, bool selected, Action selectionChanged)
    {
        Item = item;
        _isSelected = selected;
        _selectionChanged = selectionChanged;
    }

    public TodoItem Item { get; }
    public Guid Id => Item.Id;
    public string Title => Item.Title;
    public string ScheduleText => Item.PlannedStartAt is { } start && Item.DueAt is { } end
        ? $"已安排 {start.ToLocalTime():MM-dd HH:mm}–{end.ToLocalTime():HH:mm}"
        : Item.DueAt is { } due
            ? $"截止 {due.ToLocalTime():MM-dd HH:mm}"
            : Item.ReminderAt is { } reminder
                ? $"提醒 {reminder.ToLocalTime():MM-dd HH:mm}"
                : "尚未安排时间";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _selectionChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class TodayPlanDraftRowViewModel : INotifyPropertyChanged
{
    private readonly Action _inclusionChanged;
    private bool _isIncluded = true;

    public TodayPlanDraftRowViewModel(
        string title,
        DateTimeOffset sourceUpdatedAt,
        TodayPlanBlock block,
        Action inclusionChanged)
    {
        Title = title;
        SourceUpdatedAt = sourceUpdatedAt;
        Block = block;
        _inclusionChanged = inclusionChanged;
    }

    public TodayPlanBlock Block { get; }
    public Guid Id => Block.Id;
    public string Title { get; }
    public DateTimeOffset SourceUpdatedAt { get; }
    public string TimeText => $"{Block.StartAt.ToLocalTime():HH:mm}–{Block.EndAt.ToLocalTime():HH:mm}";
    public string Reason => Block.Reason;
    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value) return;
            _isIncluded = value;
            OnPropertyChanged();
            _inclusionChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class TodoViewModel
{
    private CancellationTokenSource? _todayPlanCts;
    private Task? _todayPlanTask;
    private bool _isTodayPlanGenerating;
    private bool _changingTodayPlanSelection;
    private string _todayPlanMessage = "勾选待处理事项后，可生成今天的时间块草稿。";
    private string _todayPlanError = string.Empty;
    private TodayPlanUndoRecord? _todayPlanUndo;

    public ObservableCollection<TodayPlanCandidateViewModel> TodayPlanCandidates { get; } = new();
    public ObservableCollection<TodayPlanDraftRowViewModel> TodayPlanDraft { get; } = new();

    public int TodayPlanSelectedCount => TodayPlanCandidates.Count(item => item.IsSelected);
    public bool HasTodayPlanCandidates => TodayPlanCandidates.Count > 0;
    public bool HasTodayPlanDraft => TodayPlanDraft.Count > 0;
    public bool HasTodayPlanError => !string.IsNullOrWhiteSpace(TodayPlanError);
    public bool IsTodayPlanGenerating => _isTodayPlanGenerating;
    public string TodayPlanAiButtonLabel => IsTodayPlanGenerating ? "正在安排…" : "AI 安排今天";
    public bool CanUndoTodayPlan => _todayPlanUndo is not null;
    public string TodayPlanUndoLabel => _todayPlanUndo is null ? "撤销安排" : $"撤销：{_todayPlanUndo.Count} 项安排";

    public string TodayPlanMessage
    {
        get => _todayPlanMessage;
        private set
        {
            if (_todayPlanMessage == value) return;
            _todayPlanMessage = value;
            OnPropertyChanged();
        }
    }

    public string TodayPlanError
    {
        get => _todayPlanError;
        private set
        {
            if (_todayPlanError == value) return;
            _todayPlanError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTodayPlanError));
        }
    }

    public ICommand GenerateAiTodayPlanCommand { get; private set; } = null!;
    public ICommand GenerateLocalTodayPlanCommand { get; private set; } = null!;
    public ICommand ApplyTodayPlanCommand { get; private set; } = null!;
    public ICommand CancelTodayPlanCommand { get; private set; } = null!;
    public ICommand UndoTodayPlanCommand { get; private set; } = null!;
    public ICommand SelectFirstTodayPlanCandidatesCommand { get; private set; } = null!;
    public ICommand ClearTodayPlanSelectionCommand { get; private set; } = null!;

    private void InitializeTodayPlanCommands()
    {
        GenerateAiTodayPlanCommand = new RelayCommand(
            async _ => await RunTodayPlanGenerationAsync(useAi: true),
            _ => CanGenerateTodayPlan() && _todayPlanAiClient is not null);
        GenerateLocalTodayPlanCommand = new RelayCommand(
            async _ => await RunTodayPlanGenerationAsync(useAi: false),
            _ => CanGenerateTodayPlan());
        ApplyTodayPlanCommand = new RelayCommand(_ => ApplyTodayPlan(), _ => CanApplyTodayPlan());
        CancelTodayPlanCommand = new RelayCommand(_ => ClearTodayPlanDraft("安排草稿已取消，待办未改变。"), _ => HasTodayPlanDraft);
        UndoTodayPlanCommand = new RelayCommand(_ => UndoTodayPlan(), _ => CanUndoTodayPlan);
        SelectFirstTodayPlanCandidatesCommand = new RelayCommand(
            _ => SelectFirstTodayPlanCandidates(),
            _ => HasTodayPlanCandidates && TodayPlanSelectedCount != Math.Min(12, TodayPlanCandidates.Count));
        ClearTodayPlanSelectionCommand = new RelayCommand(
            _ => ClearTodayPlanSelection(),
            _ => TodayPlanSelectedCount > 0);
    }

    public void SetTodayPlanClient(ITodayPlanAiClient client)
    {
        _todayPlanAiClient = client ?? throw new ArgumentNullException(nameof(client));
        RaiseTodayPlanCommands();
    }

    private bool CanGenerateTodayPlan() =>
        _store is not null && !_isTodayPlanGenerating && TodayPlanSelectedCount is >= 1 and <= 12;

    private bool CanApplyTodayPlan() =>
        _store is not null && !_isTodayPlanGenerating && TodayPlanDraft.Any(item => item.IsIncluded);

    private async Task RunTodayPlanGenerationAsync(bool useAi)
    {
        var task = GenerateTodayPlanAsync(useAi);
        _todayPlanTask = task;
        try { await task; }
        finally
        {
            if (ReferenceEquals(_todayPlanTask, task)) _todayPlanTask = null;
        }
    }

    public async Task GenerateTodayPlanAsync(bool useAi)
    {
        if (!CanGenerateTodayPlan())
        {
            TodayPlanError = TodayPlanSelectedCount > 12 ? "一次最多选择 12 条事项。" : "请先勾选至少一条待处理事项。";
            return;
        }

        var request = CreateTodayPlanRequest();
        _isTodayPlanGenerating = true;
        TodayPlanError = string.Empty;
        TodayPlanDraft.Clear();
        TodayPlanMessage = useAi
            ? $"正在把你勾选的 {request.Items.Count} 条事项发送给当前 AI 配置…"
            : $"正在本地安排 {request.Items.Count} 条事项，不访问网络。";
        RaiseTodayPlanStateChanged();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _todayPlanCts = timeout;
        try
        {
            TodayPlanResult result;
            if (useAi)
            {
                var connection = _connectionProvider?.Invoke();
                if (connection is null || _todayPlanAiClient is null)
                {
                    TodayPlanError = "尚未保存通过测试的 AI 配置。";
                    TodayPlanMessage = "可先到设置完成 AI 配置，或直接使用本地安排。";
                    return;
                }
                result = await _todayPlanAiClient.PlanTodayAsync(
                    connection.Endpoint,
                    connection.Model,
                    connection.ApiKey,
                    request,
                    timeout.Token);
            }
            else
            {
                result = OpenAiCompatibleTodoClient.CreateLocalTodayPlan(request);
                await Task.CompletedTask;
            }

            if (result.Status == TodayPlanStatus.Failed)
            {
                TodayPlanError = string.Join(" ", new[] { result.ErrorMessage, result.Suggestion }
                    .Where(message => !string.IsNullOrWhiteSpace(message)));
                TodayPlanMessage = "安排未生成，待办数据未改变。";
                return;
            }

            var inputs = request.Items.ToDictionary(item => item.Id);
            foreach (var block in result.Blocks.OrderBy(block => block.StartAt))
            {
                var input = inputs[block.Id];
                TodayPlanDraft.Add(new TodayPlanDraftRowViewModel(
                    input.Title,
                    input.UpdatedAt,
                    block,
                    OnTodayPlanInclusionChanged));
            }
            var avoided = request.OccupiedBlocks?.Count ?? 0;
            var avoidanceText = avoided > 0 ? $"已避让 {avoided} 个既有时间段。" : string.Empty;
            TodayPlanMessage = useAi
                ? $"AI 安排草稿已生成。{avoidanceText}逐项核对并勾选后再确认应用。"
                : $"本地安排草稿已生成。{avoidanceText}逐项核对并勾选后再确认应用。";
        }
        catch (OperationCanceledException)
        {
            TodayPlanError = "安排已取消或超时。";
            TodayPlanMessage = "待办数据未改变。";
        }
        finally
        {
            if (ReferenceEquals(_todayPlanCts, timeout)) _todayPlanCts = null;
            _isTodayPlanGenerating = false;
            RaiseTodayPlanStateChanged();
        }
    }

    private TodayPlanRequest CreateTodayPlanRequest()
    {
        var localNow = _now();
        var items = TodayPlanCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => new TodayPlanItemInput(
                candidate.Item.Id,
                candidate.Item.Title,
                candidate.Item.Notes,
                candidate.Item.DueAt,
                candidate.Item.UpdatedAt))
            .ToArray();
        var occupied = MergeOccupiedBlocks(TodayPlanCandidates
            .Where(candidate => !candidate.IsSelected
                && candidate.Item.PlannedStartAt is not null
                && candidate.Item.DueAt is not null)
            .Select(candidate => new TodayPlanOccupiedBlock(
                candidate.Item.PlannedStartAt!.Value.ToOffset(localNow.Offset),
                candidate.Item.DueAt!.Value.ToOffset(localNow.Offset)))
            .Where(block => block.StartAt.Date == localNow.Date
                && block.EndAt.Date == localNow.Date
                && block.EndAt > localNow
                && block.EndAt > block.StartAt));
        return new TodayPlanRequest(items, localNow, TimeZoneInfo.Local.Id, OccupiedBlocks: occupied);
    }

    private static IReadOnlyList<TodayPlanOccupiedBlock> MergeOccupiedBlocks(
        IEnumerable<TodayPlanOccupiedBlock> blocks)
    {
        var ordered = blocks.OrderBy(block => block.StartAt).ThenBy(block => block.EndAt).ToArray();
        if (ordered.Length == 0) return Array.Empty<TodayPlanOccupiedBlock>();
        var merged = new List<TodayPlanOccupiedBlock> { ordered[0] };
        foreach (var block in ordered.Skip(1))
        {
            var last = merged[^1];
            if (block.StartAt <= last.EndAt)
                merged[^1] = last with { EndAt = block.EndAt > last.EndAt ? block.EndAt : last.EndAt };
            else
                merged.Add(block);
        }
        return merged;
    }

    private void ApplyTodayPlan()
    {
        if (_store is null) return;
        var included = TodayPlanDraft.Where(item => item.IsIncluded).ToArray();
        if (included.Length == 0) return;
        try
        {
            var current = _store.Load().ToDictionary(item => item.Id);
            var before = new List<TodoItem>(included.Length);
            var requests = new List<TodoBatchUpdate>(included.Length);
            foreach (var row in included)
            {
                if (!current.TryGetValue(row.Id, out var item))
                    throw new TodoValidationException("待办不存在或已被删除。");
                before.Add(item);
                requests.Add(new TodoBatchUpdate(item with
                {
                    PlannedStartAt = row.Block.StartAt,
                    DueAt = row.Block.EndAt,
                }, row.SourceUpdatedAt));
            }
            var updated = _store.UpdateBatch(requests);
            _todayPlanUndo = new TodayPlanUndoRecord(
                before.Zip(updated, (snapshot, changed) => new TodoBatchUpdate(snapshot, changed.UpdatedAt)).ToArray());
            ClearTodayPlanSelection();
            ClearTodayPlanDraft($"已应用 {updated.Count} 项今日安排；截止时间同步为时间块结束。", clearError: true);
            Reload();
            Status = $"已确认并保存 {updated.Count} 项今日安排。";
            RaiseTodayPlanStateChanged();
        }
        catch (TodoValidationException ex)
        {
            TodayPlanError = ex.Message;
            TodayPlanMessage = "整批未写入；请刷新或重新生成安排。";
        }
        catch
        {
            TodayPlanError = "安排保存失败，原有待办仍保留。";
            TodayPlanMessage = "整批未写入。";
        }
    }

    private void UndoTodayPlan()
    {
        if (_store is null || _todayPlanUndo is null) return;
        try
        {
            var restored = _store.RestoreBatch(_todayPlanUndo.Entries);
            _todayPlanUndo = null;
            Reload();
            Status = $"已撤销 {restored.Count} 项今日安排。";
            TodayPlanMessage = "最近一次批量安排已撤销。";
            TodayPlanError = string.Empty;
        }
        catch (TodoValidationException ex)
        {
            TodayPlanError = ex.Message;
            TodayPlanMessage = "撤销未执行；待办可能已再次修改。";
        }
        catch
        {
            TodayPlanError = "撤销失败，当前待办保持不变。";
        }
        RaiseTodayPlanStateChanged();
    }

    private void RefreshTodayPlanCandidates()
    {
        var selected = TodayPlanCandidates.Where(item => item.IsSelected).Select(item => item.Id).ToHashSet();
        TodayPlanCandidates.Clear();
        if (_store is not null)
        {
            foreach (var item in _store.Load()
                .Where(item => item.Status == TodoStatus.Pending)
                .OrderBy(item => item.PlannedStartAt ?? item.DueAt ?? item.ReminderAt ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.CreatedAt))
                TodayPlanCandidates.Add(new TodayPlanCandidateViewModel(item, selected.Contains(item.Id), OnTodayPlanSelectionChanged));
        }
        RaiseTodayPlanStateChanged();
    }

    private void OnTodayPlanSelectionChanged()
    {
        if (_changingTodayPlanSelection) return;
        if (HasTodayPlanDraft) ClearTodayPlanDraft("选择已改变，请重新生成安排。", clearError: true);
        if (TodayPlanSelectedCount > 12)
            TodayPlanError = "一次最多选择 12 条事项，请取消多余勾选。";
        else if (TodayPlanError.StartsWith("一次最多选择", StringComparison.Ordinal))
            TodayPlanError = string.Empty;
        OnPropertyChanged(nameof(TodayPlanSelectedCount));
        RaiseTodayPlanCommands();
    }

    private void OnTodayPlanInclusionChanged() => RaiseTodayPlanCommands();

    private void ClearTodayPlanSelection()
    {
        ChangeTodayPlanSelection((_, _) => false);
    }

    private void SelectFirstTodayPlanCandidates() =>
        ChangeTodayPlanSelection((_, index) => index < 12);

    private void ChangeTodayPlanSelection(Func<TodayPlanCandidateViewModel, int, bool> selector)
    {
        _changingTodayPlanSelection = true;
        try
        {
            for (var index = 0; index < TodayPlanCandidates.Count; index++)
                TodayPlanCandidates[index].IsSelected = selector(TodayPlanCandidates[index], index);
        }
        finally
        {
            _changingTodayPlanSelection = false;
        }
        OnTodayPlanSelectionChanged();
    }

    private void ClearTodayPlanDraft(string message, bool clearError = false)
    {
        TodayPlanDraft.Clear();
        TodayPlanMessage = message;
        if (clearError) TodayPlanError = string.Empty;
        RaiseTodayPlanStateChanged();
    }

    private void CancelTodayPlanWork() => _todayPlanCts?.Cancel();

    private void AddTodayPlanBackgroundTask(List<Task> tasks)
    {
        if (_todayPlanTask is { IsCompleted: false }) tasks.Add(_todayPlanTask);
    }

    private void RaiseTodayPlanStateChanged()
    {
        OnPropertyChanged(nameof(TodayPlanSelectedCount));
        OnPropertyChanged(nameof(HasTodayPlanCandidates));
        OnPropertyChanged(nameof(HasTodayPlanDraft));
        OnPropertyChanged(nameof(HasTodayPlanError));
        OnPropertyChanged(nameof(IsTodayPlanGenerating));
        OnPropertyChanged(nameof(TodayPlanAiButtonLabel));
        OnPropertyChanged(nameof(CanUndoTodayPlan));
        OnPropertyChanged(nameof(TodayPlanUndoLabel));
        RaiseTodayPlanCommands();
    }

    private void RaiseTodayPlanCommands()
    {
        (GenerateAiTodayPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateLocalTodayPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyTodayPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelTodayPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UndoTodayPlanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SelectFirstTodayPlanCandidatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearTodayPlanSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private sealed record TodayPlanUndoRecord(IReadOnlyList<TodoBatchUpdate> Entries)
    {
        public int Count => Entries.Count;
    }
}
