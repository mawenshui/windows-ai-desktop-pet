using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed record TodoPageModeOption(string Id, string DisplayName);

public sealed record DailyJournalRowViewModel(
    Guid Id,
    string Title,
    bool IsCompleted,
    bool WasPlanned,
    string StateText,
    string PlanText);

public sealed partial class TodoViewModel
{
    private readonly SemaphoreSlim _journalSaveGate = new(1, 1);
    private DailyJournalStore? _journalStore;
    private CancellationTokenSource? _journalSaveCts;
    private Task? _journalSaveTask;
    private TodoPageModeOption? _selectedTodoPageMode;
    private DateOnly _selectedJournalDate;
    private string _journalNote = string.Empty;
    private string _journalSavedNote = string.Empty;
    private long _journalRevision;
    private bool _loadingJournal;
    private bool _isHistoricalJournalEditing;
    private bool _showDeleteJournalConfirmation;
    private string _journalSaveStatus = "复盘会自动保存在本机";
    private int _journalCompletedCount;
    private int _journalPendingCount;
    private int _journalPlannedCount;
    private FocusDailySummary _journalFocusSummary = new(0, 0, 0);

    public IReadOnlyList<TodoPageModeOption> TodoPageModes { get; } = new[]
    {
        new TodoPageModeOption("list", "清单"),
        new TodoPageModeOption("journal", "复盘"),
    };

    public ObservableCollection<DailyJournalRowViewModel> JournalItems { get; } = new();

    public TodoPageModeOption SelectedTodoPageMode
    {
        get => _selectedTodoPageMode ??= TodoPageModes[0];
        set
        {
            if (value is null || ReferenceEquals(_selectedTodoPageMode, value)) return;
            _selectedTodoPageMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTodoListMode));
            OnPropertyChanged(nameof(IsJournalMode));
            if (IsJournalMode && _journalStore is not null) LoadJournalDate(_selectedJournalDate);
        }
    }

    public bool IsTodoListMode => !string.Equals(SelectedTodoPageMode.Id, "journal", StringComparison.Ordinal);
    public bool IsJournalMode => !IsTodoListMode;

    public DateOnly SelectedJournalDate => _selectedJournalDate;
    public string JournalDateText => _selectedJournalDate.ToString("yyyy 年 M 月 d 日 · dddd", CultureInfo.GetCultureInfo("zh-CN"));
    public bool IsJournalToday => _selectedJournalDate == TodayDate;
    public bool IsHistoricalJournal => _selectedJournalDate < TodayDate;
    public bool CanGoToNextJournalDate => _selectedJournalDate < TodayDate;
    public bool IsJournalReadOnly => IsHistoricalJournal && !_isHistoricalJournalEditing;
    public bool CanEditHistoricalJournal => IsHistoricalJournal && !_isHistoricalJournalEditing;
    public bool IsHistoricalJournalEditing => _isHistoricalJournalEditing;
    public bool ShowDeleteJournalConfirmation => _showDeleteJournalConfirmation;
    public bool HasJournalItems => JournalItems.Count > 0;
    public int JournalCompletedCount => _journalCompletedCount;
    public int JournalPendingCount => _journalPendingCount;
    public int JournalPlannedCount => _journalPlannedCount;
    public string JournalSummaryText => $"已完成 {JournalCompletedCount} · 待处理 {JournalPendingCount} · 已安排 {JournalPlannedCount} · 专注 {_journalFocusSummary.CompletedCount} 次 / {FormatFocusDuration(_journalFocusSummary.TotalSeconds)}";
    public string JournalSaveStatus
    {
        get => _journalSaveStatus;
        private set { if (_journalSaveStatus == value) return; _journalSaveStatus = value; OnPropertyChanged(); }
    }

    public string JournalNote
    {
        get => _journalNote;
        set
        {
            value ??= string.Empty;
            if (_journalNote == value) return;
            _journalNote = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JournalCharacterCountText));
            if (!_loadingJournal) ScheduleJournalSave();
        }
    }

    public string JournalCharacterCountText => $"{JournalNote.Length}/4000";

    public ICommand PreviousJournalDateCommand { get; private set; } = null!;
    public ICommand NextJournalDateCommand { get; private set; } = null!;
    public ICommand TodayJournalDateCommand { get; private set; } = null!;
    public ICommand SaveJournalCommand { get; private set; } = null!;
    public ICommand EditHistoricalJournalCommand { get; private set; } = null!;
    public ICommand StopEditingHistoricalJournalCommand { get; private set; } = null!;
    public ICommand RequestDeleteJournalCommand { get; private set; } = null!;
    public ICommand ConfirmDeleteJournalCommand { get; private set; } = null!;
    public ICommand CancelDeleteJournalCommand { get; private set; } = null!;

    private DateOnly TodayDate => DateOnly.FromDateTime(_now().ToLocalTime().DateTime);

    private void InitializeJournalCommands()
    {
        _selectedTodoPageMode = TodoPageModes[0];
        PreviousJournalDateCommand = new RelayCommand(async _ => await NavigateJournalAsync(-1));
        NextJournalDateCommand = new RelayCommand(async _ => await NavigateJournalAsync(1), _ => CanGoToNextJournalDate);
        TodayJournalDateCommand = new RelayCommand(async _ => await NavigateJournalToAsync(TodayDate), _ => !IsJournalToday);
        SaveJournalCommand = new RelayCommand(async _ => await SaveJournalNowAsync());
        EditHistoricalJournalCommand = new RelayCommand(_ => SetHistoricalJournalEditing(true), _ => CanEditHistoricalJournal);
        StopEditingHistoricalJournalCommand = new RelayCommand(async _ =>
        {
            if (await SaveJournalNowAsync()) SetHistoricalJournalEditing(false);
        }, _ => IsHistoricalJournalEditing);
        RequestDeleteJournalCommand = new RelayCommand(_ => SetDeleteJournalConfirmation(true));
        ConfirmDeleteJournalCommand = new RelayCommand(_ => DeleteSelectedJournal(), _ => ShowDeleteJournalConfirmation);
        CancelDeleteJournalCommand = new RelayCommand(_ => SetDeleteJournalConfirmation(false), _ => ShowDeleteJournalConfirmation);
    }

    private void AttachJournal(DailyJournalStore journalStore)
    {
        _journalStore = journalStore;
        _selectedJournalDate = TodayDate;
        try
        {
            var document = journalStore.Load();
            if (document.ActiveDate is { } active && active < TodayDate)
            {
                var projection = ProjectJournal(active);
                journalStore.FinalizeAndActivate(active, TodayDate, projection.Items);
            }
            else
            {
                journalStore.Activate(TodayDate);
            }
            LoadJournalDate(_selectedJournalDate);
        }
        catch
        {
            JournalSaveStatus = "复盘数据暂时无法读取；原文件已保留";
            RefreshJournalProjection();
        }
    }

    public async Task RefreshJournalDateAsync()
    {
        if (_journalStore is null) return;
        var today = TodayDate;
        var document = _journalStore.Load();
        var previousActive = document.ActiveDate;
        if (previousActive is { } active && active < today)
        {
            if (_selectedJournalDate == active && !await SaveJournalNowAsync()) return;
            var projection = ProjectJournal(active);
            await Task.Run(() => _journalStore.FinalizeAndActivate(active, today, projection.Items));
        }
        else
        {
            await Task.Run(() => _journalStore.Activate(today));
        }
        if (previousActive == _selectedJournalDate) LoadJournalDate(today);
        RaiseJournalStateChanged();
    }

    public async Task ExportSelectedJournalAsync(string path)
    {
        if (_journalStore is null) throw new InvalidOperationException("复盘存储尚未就绪。");
        try
        {
            await SaveJournalNowAsync();
            var entry = BuildSelectedJournalEntry();
            var focusSummary = _focusService?.Summarize(_selectedJournalDate) ?? new FocusDailySummary(0, 0, 0);
            await Task.Run(() => DailyJournalMarkdownExporter.Export(path, entry, focusSummary));
            JournalSaveStatus = "Markdown 已导出";
        }
        catch
        {
            JournalSaveStatus = "Markdown 导出失败；原复盘仍保留";
            throw;
        }
    }

    public bool HandleJournalEscape()
    {
        if (!IsJournalMode) return false;
        if (ShowDeleteJournalConfirmation)
        {
            SetDeleteJournalConfirmation(false);
            return true;
        }
        if (IsHistoricalJournalEditing)
        {
            SaveJournalCommand.Execute(null);
            SetHistoricalJournalEditing(false);
            return true;
        }
        SelectedTodoPageMode = TodoPageModes[0];
        return true;
    }

    private async Task NavigateJournalAsync(int days) =>
        await NavigateJournalToAsync(_selectedJournalDate.AddDays(days));

    private async Task NavigateJournalToAsync(DateOnly date)
    {
        if (date > TodayDate) return;
        if (!await SaveJournalNowAsync()) return;
        LoadJournalDate(date);
    }

    private void LoadJournalDate(DateOnly date)
    {
        _journalSaveCts?.Cancel();
        _selectedJournalDate = date > TodayDate ? TodayDate : date;
        _isHistoricalJournalEditing = false;
        _showDeleteJournalConfirmation = false;
        var entry = _journalStore?.Get(_selectedJournalDate);
        _journalRevision = entry?.Revision ?? 0;
        _loadingJournal = true;
        JournalNote = entry?.Note ?? string.Empty;
        _loadingJournal = false;
        _journalSavedNote = JournalNote;
        JournalSaveStatus = entry is null ? "复盘会自动保存在本机" : $"已保存 · {entry.UpdatedAt.ToLocalTime():HH:mm}";
        RefreshJournalProjection(entry);
        RaiseJournalStateChanged();
    }

    private void RefreshJournalProjection(DailyJournalEntry? stored = null)
    {
        if (_journalStore is null || _selectedJournalDate == default) return;
        stored ??= _journalStore.Get(_selectedJournalDate);
        var projection = stored?.FinalizedAt is not null
            ? ProjectionFromSnapshot(stored)
            : ProjectJournal(_selectedJournalDate);
        JournalItems.Clear();
        foreach (var item in projection.Items)
            JournalItems.Add(new DailyJournalRowViewModel(
                item.Id,
                item.Title,
                item.IsCompleted,
                item.WasPlanned,
                item.IsCompleted ? "已完成" : "待处理",
                item.WasPlanned ? "已安排" : string.Empty));
        _journalCompletedCount = projection.CompletedCount;
        _journalPendingCount = projection.PendingCount;
        _journalPlannedCount = projection.PlannedCount;
        _journalFocusSummary = _focusService?.Summarize(_selectedJournalDate) ?? new FocusDailySummary(0, 0, 0);
        OnPropertyChanged(nameof(HasJournalItems));
        OnPropertyChanged(nameof(JournalCompletedCount));
        OnPropertyChanged(nameof(JournalPendingCount));
        OnPropertyChanged(nameof(JournalPlannedCount));
        OnPropertyChanged(nameof(JournalSummaryText));
    }

    private static string FormatFocusDuration(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        return minutes >= 60 ? $"{minutes / 60} 小时 {minutes % 60} 分" : $"{minutes} 分";
    }

    private DailyJournalProjection ProjectJournal(DateOnly date) => DailyJournalProjector.Project(
        date,
        _store?.Load() ?? Array.Empty<TodoItem>(),
        TimeZoneInfo.Local);

    private static DailyJournalProjection ProjectionFromSnapshot(DailyJournalEntry entry) => new(
        entry.Date,
        entry.Snapshot.Count(item => item.IsCompleted),
        entry.Snapshot.Count(item => !item.IsCompleted),
        entry.Snapshot.Count(item => item.WasPlanned),
        entry.Snapshot);

    private void ScheduleJournalSave()
    {
        _journalSaveCts?.Cancel();
        _journalSaveCts?.Dispose();
        _journalSaveCts = new CancellationTokenSource();
        var token = _journalSaveCts.Token;
        JournalSaveStatus = JournalNote.Length > 4000 ? "复盘内容不能超过 4000 个字符" : "正在等待自动保存…";
        _journalSaveTask = SaveAfterDelayAsync(token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            await SaveJournalCoreAsync(token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> SaveJournalNowAsync()
    {
        _journalSaveCts?.Cancel();
        return await SaveJournalCoreAsync(CancellationToken.None);
    }

    private async Task<bool> SaveJournalCoreAsync(CancellationToken token)
    {
        if (_journalStore is null || _loadingJournal || JournalNote.Length > 4000) return false;
        if (IsJournalReadOnly || JournalNote == _journalSavedNote) return true;
        await _journalSaveGate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var date = _selectedJournalDate;
            var note = JournalNote;
            var expectedRevision = _journalRevision;
            var historical = IsHistoricalJournal;
            var saved = await Task.Run(() => historical
                ? _journalStore.SaveHistoricalNote(date, note, expectedRevision)
                : _journalStore.SaveNote(date, note, expectedRevision), token);
            if (date == _selectedJournalDate)
            {
                _journalRevision = saved.Revision;
                _journalSavedNote = note;
                JournalSaveStatus = $"已保存 · {saved.UpdatedAt.ToLocalTime():HH:mm}";
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            JournalSaveStatus = "自动保存失败；请按 Ctrl+S 重试";
            return false;
        }
        finally
        {
            _journalSaveGate.Release();
        }
    }

    private DailyJournalEntry BuildSelectedJournalEntry()
    {
        var stored = _journalStore?.Get(_selectedJournalDate);
        var projection = stored?.FinalizedAt is not null ? ProjectionFromSnapshot(stored) : ProjectJournal(_selectedJournalDate);
        return (stored ?? new DailyJournalEntry { Date = _selectedJournalDate, UpdatedAt = _now() }) with
        {
            Note = JournalNote,
            Snapshot = projection.Items,
        };
    }

    private void SetHistoricalJournalEditing(bool enabled)
    {
        _isHistoricalJournalEditing = enabled;
        OnPropertyChanged(nameof(IsJournalReadOnly));
        OnPropertyChanged(nameof(CanEditHistoricalJournal));
        OnPropertyChanged(nameof(IsHistoricalJournalEditing));
        RaiseJournalCommandStates();
    }

    private void SetDeleteJournalConfirmation(bool visible)
    {
        _showDeleteJournalConfirmation = visible;
        OnPropertyChanged(nameof(ShowDeleteJournalConfirmation));
        RaiseJournalCommandStates();
    }

    private void DeleteSelectedJournal()
    {
        if (_journalStore is null) return;
        _journalSaveCts?.Cancel();
        try
        {
            _journalStore.Delete(_selectedJournalDate);
            LoadJournalDate(_selectedJournalDate);
            JournalSaveStatus = "这一天的复盘已删除；待办未受影响";
        }
        catch
        {
            JournalSaveStatus = "删除失败；原复盘和待办均已保留";
        }
    }

    private void RaiseJournalStateChanged()
    {
        OnPropertyChanged(nameof(SelectedJournalDate));
        OnPropertyChanged(nameof(JournalDateText));
        OnPropertyChanged(nameof(IsJournalToday));
        OnPropertyChanged(nameof(IsHistoricalJournal));
        OnPropertyChanged(nameof(CanGoToNextJournalDate));
        OnPropertyChanged(nameof(IsJournalReadOnly));
        OnPropertyChanged(nameof(CanEditHistoricalJournal));
        OnPropertyChanged(nameof(IsHistoricalJournalEditing));
        OnPropertyChanged(nameof(ShowDeleteJournalConfirmation));
        RaiseJournalCommandStates();
    }

    private void RaiseJournalCommandStates()
    {
        foreach (var command in new[]
        {
            NextJournalDateCommand, TodayJournalDateCommand, EditHistoricalJournalCommand,
            StopEditingHistoricalJournalCommand, ConfirmDeleteJournalCommand, CancelDeleteJournalCommand,
        })
            (command as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void AddJournalBackgroundTask(ICollection<Task> tasks)
    {
        if (_journalSaveTask is { IsCompleted: false } task) tasks.Add(task);
    }

    private void CancelJournalBackgroundWork()
    {
        _journalSaveCts?.Cancel();
        if (_journalStore is null || _selectedJournalDate == default || JournalNote.Length > 4000) return;
        try
        {
            var current = _journalStore.Get(_selectedJournalDate);
            var expected = current?.Revision ?? 0;
            if (current?.Note == JournalNote) return;
            if (IsHistoricalJournal) _journalStore.SaveHistoricalNote(_selectedJournalDate, JournalNote, expected);
            else _journalStore.SaveNote(_selectedJournalDate, JournalNote, expected);
        }
        catch { /* Shutdown keeps the existing recoverable file; the UI already exposed save failures. */ }
    }
}
