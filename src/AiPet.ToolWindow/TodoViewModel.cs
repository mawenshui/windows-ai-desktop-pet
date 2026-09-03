using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AiPet.AI;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed record TodoAiConnection(string Endpoint, string Model, string ApiKey);

public sealed record TodoFilterOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record TodoRowViewModel(TodoItem Item)
{
    public Guid Id => Item.Id;
    public string Title => Item.Title;
    public string Notes => Item.Notes;
    public bool IsReminder => Item.IsReminder;
    public bool IsCompleted => Item.Status == TodoStatus.Completed;
    public bool HasReminder => Item.ReminderAt is not null;
    public bool CanRestore => IsCompleted && !IsReminder;
    public bool CanCancelReminder => HasReminder && !IsCompleted && !IsReminder;
    public string DueText => Item.DueAt is { } due
        ? $"截止 {due.ToLocalTime():MM-dd HH:mm}"
        : "无截止时间";
    public string ReminderText => Item.ReminderAt is { } reminder
        ? $"提醒 {reminder.ToLocalTime():MM-dd HH:mm}"
        : Item.ReminderState switch
        {
            ReminderState.Cancelled => "提醒已取消",
            ReminderState.Failed => "提醒投递失败",
            _ => "无提醒",
        };
    public string StateText => Item.Status == TodoStatus.Completed ? "已完成" : "待处理";
    public string ItemTypeText => Item.IsReminder ? "提醒项" : "待办";
    public string ReminderChannelText => Item.IsReminder
        ? (Item.ReminderRoamEnabled, Item.ReminderBubbleEnabled) switch
        {
            (true, true) => "桌宠漫游 + 气泡",
            (true, false) => "桌宠漫游",
            (false, true) => "桌宠气泡",
            _ => "仅托盘提醒",
        }
        : "普通待办提醒";
    public string RepeatText => "一次性";
    public string TargetChoiceText => $"{Title} · {DueText} · 创建于 {Item.CreatedAt.ToLocalTime():MM-dd HH:mm}";
    public override string ToString() => $"{Title} · {DueText}";
}

public sealed class TodoViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _defaultNow = () => DateTimeOffset.Now;
    private TodoStore? _store;
    private ITodoAiClient? _todoAiClient;
    private Func<TodoAiConnection?>? _connectionProvider;
    private Func<DateTimeOffset> _now;
    private AiTodoDraft? _aiDraft;
    private TodoItem? _aiTarget;
    private UndoRecord? _undoRecord;
    private TodoItem? _reminderAlertItem;
    private CancellationTokenSource? _aiParseCts;
    private Task? _aiParseTask;

    public TodoViewModel()
    {
        _now = _defaultNow;
        InitializeCommands();
    }

    public TodoViewModel(
        TodoStore store,
        ITodoAiClient todoAiClient,
        Func<TodoAiConnection?> connectionProvider,
        Func<DateTimeOffset>? now = null)
        : this()
    {
        Attach(store, todoAiClient, connectionProvider, now);
    }

    public ObservableCollection<TodoRowViewModel> Items { get; } = new();
    public ObservableCollection<TodoRowViewModel> AiTargetChoices { get; } = new();

    public IReadOnlyList<TodoFilterOption> Filters { get; } = new[]
    {
        new TodoFilterOption("pending", "待处理"),
        new TodoFilterOption("today", "今天"),
        new TodoFilterOption("upcoming", "未来 7 天"),
        new TodoFilterOption("completed", "已完成"),
    };

    public bool HasItems => Items.Count > 0;
    public string EmptyMessage => SelectedFilterId switch
    {
        "completed" => "还没有已完成待办，完成事项后会保留在这里。",
        "today" => "今天没有待办或提醒，可以放心安排下一件事。",
        "upcoming" => "未来 7 天没有已安排时间的待办。",
        _ => "还没有待办。可以手动新建，或用一句话让 AI 生成草稿。",
    };

    private string _selectedFilterId = "pending";
    public string SelectedFilterId
    {
        get => _selectedFilterId;
        set
        {
            if (_selectedFilterId == value || string.IsNullOrWhiteSpace(value)) return;
            _selectedFilterId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFilter));
            Reload();
        }
    }

    public TodoFilterOption? SelectedFilter
    {
        get => Filters.FirstOrDefault(filter => filter.Id == SelectedFilterId);
        set
        {
            if (value is not null) SelectedFilterId = value.Id;
        }
    }

    private string _status = "待办只保存在当前 Windows 账户。";
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    private bool _isEditorOpen;
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set
        {
            if (_isEditorOpen == value) return;
            _isEditorOpen = value;
            OnPropertyChanged();
        }
    }

    private Guid? _editingId;
    public bool IsEditing => _editingId is not null;
    public string EditorHeading => IsEditing
        ? (EditorIsReminder ? "编辑提醒项" : "编辑待办")
        : (EditorIsReminder ? "新建提醒项" : "新建待办");
    public string EditorSaveLabel => IsEditing
        ? "保存修改"
        : (EditorIsReminder ? "创建提醒项" : "创建待办");

    private string _editorTitle = string.Empty;
    public string EditorTitle
    {
        get => _editorTitle;
        set
        {
            if (_editorTitle == value) return;
            _editorTitle = value;
            EditorError = string.Empty;
            OnPropertyChanged();
        }
    }

    private string _editorNotes = string.Empty;
    public string EditorNotes
    {
        get => _editorNotes;
        set
        {
            if (_editorNotes == value) return;
            _editorNotes = value;
            OnPropertyChanged();
        }
    }

    private DateTime? _editorDueDate;
    public DateTime? EditorDueDate
    {
        get => _editorDueDate;
        set
        {
            if (_editorDueDate == value) return;
            _editorDueDate = value;
            OnPropertyChanged();
        }
    }

    private string _editorDueTime = "18:00";
    public string EditorDueTime
    {
        get => _editorDueTime;
        set
        {
            if (_editorDueTime == value) return;
            _editorDueTime = value;
            OnPropertyChanged();
        }
    }

    private DateTime? _editorReminderDate;
    public DateTime? EditorReminderDate
    {
        get => _editorReminderDate;
        set
        {
            if (_editorReminderDate == value) return;
            _editorReminderDate = value;
            OnPropertyChanged();
        }
    }

    private string _editorReminderTime = "09:00";
    public string EditorReminderTime
    {
        get => _editorReminderTime;
        set
        {
            if (_editorReminderTime == value) return;
            _editorReminderTime = value;
            OnPropertyChanged();
        }
    }

    private bool _editorIsReminder;
    /// <summary>
    /// Reminder entries are intentionally separate from ordinary todos:
    /// delivery completes them automatically and can optionally use pet
    /// roaming and/or a pet bubble.
    /// </summary>
    public bool EditorIsReminder
    {
        get => _editorIsReminder;
        set
        {
            if (_editorIsReminder == value) return;
            _editorIsReminder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditorReminderOptionsVisible));
            OnPropertyChanged(nameof(EditorHeading));
            OnPropertyChanged(nameof(EditorSaveLabel));
        }
    }

    public bool EditorReminderOptionsVisible => EditorIsReminder;

    private bool _editorReminderRoamEnabled;
    public bool EditorReminderRoamEnabled
    {
        get => _editorReminderRoamEnabled;
        set
        {
            if (_editorReminderRoamEnabled == value) return;
            _editorReminderRoamEnabled = value;
            OnPropertyChanged();
        }
    }

    private bool _editorReminderBubbleEnabled;
    public bool EditorReminderBubbleEnabled
    {
        get => _editorReminderBubbleEnabled;
        set
        {
            if (_editorReminderBubbleEnabled == value) return;
            _editorReminderBubbleEnabled = value;
            OnPropertyChanged();
        }
    }

    private string _editorError = string.Empty;
    public string EditorError
    {
        get => _editorError;
        private set
        {
            if (_editorError == value) return;
            _editorError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEditorError));
        }
    }
    public bool HasEditorError => !string.IsNullOrWhiteSpace(EditorError);

    private string _aiInput = string.Empty;
    public string AiInput
    {
        get => _aiInput;
        set
        {
            if (_aiInput == value) return;
            _aiInput = value;
            OnPropertyChanged();
            ClearAiResult(keepInput: true, keepStatus: true);
            RaiseAiCommands();
        }
    }

    private bool _isAiParsing;
    public bool IsAiParsing => _isAiParsing;
    public string AiParseButtonLabel => IsAiParsing ? "正在解析…" : "生成草稿";

    private string _aiMessage = "AI 只生成草稿；确认前不会创建或修改任何待办。";
    public string AiMessage
    {
        get => _aiMessage;
        private set
        {
            if (_aiMessage == value) return;
            _aiMessage = value;
            OnPropertyChanged();
        }
    }

    private string _aiError = string.Empty;
    public string AiError
    {
        get => _aiError;
        private set
        {
            if (_aiError == value) return;
            _aiError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAiError));
        }
    }
    public bool HasAiError => !string.IsNullOrWhiteSpace(AiError);

    private string _aiClarification = string.Empty;
    public string AiClarification
    {
        get => _aiClarification;
        private set
        {
            if (_aiClarification == value) return;
            _aiClarification = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsAiClarification));
        }
    }
    public bool NeedsAiClarification => !string.IsNullOrWhiteSpace(AiClarification);
    public bool HasAiDraft => _aiDraft is not null;
    public bool NeedsAiTarget => AiTargetChoices.Count > 1 && _aiTarget is null;
    public string AiOperationText => _aiDraft?.Operation switch
    {
        AiTodoOperation.Create => AiDraftCreatesReminder ? "创建提醒项" : "创建待办",
        AiTodoOperation.Update => "修改待办",
        AiTodoOperation.Complete => "完成待办",
        AiTodoOperation.Delete => "删除待办",
        AiTodoOperation.Snooze => "稍后提醒",
        _ => "待确认操作",
    };
    public string AiDraftTitle => _aiDraft?.Title ?? _aiTarget?.Title ?? "未提供";
    public string AiDraftDueText => FormatAbsolute(_aiDraft?.DueAt, _aiDraft?.ClearDue == true ? "清除截止时间" : "不设置截止时间");
    public string AiDraftReminderText => FormatAbsolute(_aiDraft?.ReminderAt, _aiDraft?.ClearReminder == true ? "取消提醒" : "不设置提醒");
    public string AiDraftNotes => string.IsNullOrWhiteSpace(_aiDraft?.Notes) ? "无备注" : _aiDraft!.Notes!;
    public string AiTimeZoneText => TimeZoneInfo.Local.DisplayName;
    public string AiChangeSummary => BuildAiChangeSummary();

    private TodoRowViewModel? _selectedAiTarget;
    public TodoRowViewModel? SelectedAiTarget
    {
        get => _selectedAiTarget;
        set
        {
            if (_selectedAiTarget == value) return;
            _selectedAiTarget = value;
            _aiTarget = value?.Item;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAiTargetId));
            RaiseAiStateChanged();
        }
    }

    /// <summary>
    /// Stable ID projection retained for command and automation consumers;
    /// the UI binds the selected row object so its visible text and behavior
    /// stay in sync.
    /// </summary>
    public Guid? SelectedAiTargetId
    {
        get => _selectedAiTarget?.Id;
        set
        {
            var next = value is { } id
                ? AiTargetChoices.FirstOrDefault(row => row.Id == id)
                : null;
            if (ReferenceEquals(_selectedAiTarget, next)) return;
            SelectedAiTarget = next;
        }
    }

    public bool CanUndoAiAction => _undoRecord is not null;
    public string UndoLabel => _undoRecord is null ? "撤销" : $"撤销：{_undoRecord.Label}";

    public bool HasReminderAlert => _reminderAlertItem is not null;
    public string ReminderAlertTitle => _reminderAlertItem?.Title ?? string.Empty;
    public string ReminderAlertMessage { get; private set; } = string.Empty;

    public ICommand NewTodoCommand { get; private set; } = null!;
    public ICommand EditTodoCommand { get; private set; } = null!;
    public ICommand CancelEditorCommand { get; private set; } = null!;
    public ICommand SaveEditorCommand { get; private set; } = null!;
    public ICommand CompleteTodoCommand { get; private set; } = null!;
    public ICommand RestoreTodoCommand { get; private set; } = null!;
    public ICommand CancelReminderCommand { get; private set; } = null!;
    public ICommand SnoozeTodoCommand { get; private set; } = null!;
    public ICommand ParseAiCommand { get; private set; } = null!;
    public ICommand ConfirmAiCommand { get; private set; } = null!;
    public ICommand CancelAiCommand { get; private set; } = null!;
    public ICommand UseManualCommand { get; private set; } = null!;
    public ICommand ClearAiCommand { get; private set; } = null!;
    public ICommand UndoAiCommand { get; private set; } = null!;
    public ICommand CompleteReminderAlertCommand { get; private set; } = null!;
    public ICommand SnoozeReminderAlertCommand { get; private set; } = null!;
    public ICommand OpenReminderAlertCommand { get; private set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Attach(
        TodoStore store,
        ITodoAiClient todoAiClient,
        Func<TodoAiConnection?> connectionProvider,
        Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _todoAiClient = todoAiClient;
        _connectionProvider = connectionProvider;
        _now = now ?? _defaultNow;
        Reload();
        RaiseAllCommands();
    }

    public void ShowReminder(ReminderNotification notification)
    {
        if (notification.Item.IsReminder)
        {
            // Reminder-only entries use the pet channels and auto-complete at
            // delivery.  They must not expose the ordinary todo action bar,
            // whose complete/snooze semantics intentionally differ.
            DismissReminderAlert();
            Status = notification.IsRecovery
                ? "提醒项已补发并自动完成。"
                : "提醒项已触发并自动完成。";
            return;
        }

        _reminderAlertItem = notification.Item;
        ReminderAlertMessage = notification.IsRecovery
            ? $"补发提醒 · 原定 {notification.Item.ReminderAt?.ToLocalTime():yyyy-MM-dd HH:mm}"
            : $"提醒时间 {notification.Item.ReminderAt?.ToLocalTime():yyyy-MM-dd HH:mm}";
        OnPropertyChanged(nameof(HasReminderAlert));
        OnPropertyChanged(nameof(ReminderAlertTitle));
        OnPropertyChanged(nameof(ReminderAlertMessage));
        Status = notification.IsRecovery ? "有一条错过的提醒已补发。" : "有一条待办提醒到期。";
    }

    /// <summary>Refreshes the visible list after a scheduler state transition.</summary>
    public void RefreshItems() => Reload();

    public bool DeleteTodo(Guid id)
    {
        if (_store is null) return false;
        try
        {
            var deleted = _store.Delete(id);
            if (deleted is null) return false;
            if (_reminderAlertItem?.Id == id) DismissReminderAlert();
            Reload();
            Status = "待办已删除。";
            return true;
        }
        catch
        {
            Status = "删除失败，原有待办仍保留。";
            return false;
        }
    }

    private void InitializeCommands()
    {
        NewTodoCommand = new RelayCommand(_ => OpenNewEditor());
        EditTodoCommand = new RelayCommand(parameter => OpenEditor(Find(parameter)), parameter => Find(parameter) is not null);
        CancelEditorCommand = new RelayCommand(_ => CloseEditor());
        SaveEditorCommand = new RelayCommand(_ => SaveEditor(), _ => _store is not null);
        CompleteTodoCommand = new RelayCommand(parameter => Complete(Find(parameter)), parameter => Find(parameter)?.Status == TodoStatus.Pending);
        RestoreTodoCommand = new RelayCommand(parameter => Restore(Find(parameter)), parameter =>
            Find(parameter) is { Status: TodoStatus.Completed, IsReminder: false });
        CancelReminderCommand = new RelayCommand(parameter => CancelReminder(Find(parameter)), parameter =>
            Find(parameter) is { Status: TodoStatus.Pending, IsReminder: false, ReminderAt: not null });
        SnoozeTodoCommand = new RelayCommand(parameter => Snooze(Find(parameter)), parameter => Find(parameter)?.Status == TodoStatus.Pending);
        ParseAiCommand = new RelayCommand(async _ => await RunParseAiAsync(), _ => CanParseAi());
        ConfirmAiCommand = new RelayCommand(_ => ConfirmAi(), _ => CanConfirmAi());
        CancelAiCommand = new RelayCommand(_ => ClearAiResult(keepInput: true));
        UseManualCommand = new RelayCommand(_ => UseManualFallback(), _ => !string.IsNullOrWhiteSpace(AiInput));
        ClearAiCommand = new RelayCommand(_ => ClearAiResult(keepInput: false));
        UndoAiCommand = new RelayCommand(_ => UndoAi(), _ => CanUndoAiAction);
        CompleteReminderAlertCommand = new RelayCommand(_ => CompleteReminderAlert(), _ => HasReminderAlert);
        SnoozeReminderAlertCommand = new RelayCommand(_ => SnoozeReminderAlert(), _ => HasReminderAlert);
        OpenReminderAlertCommand = new RelayCommand(_ => OpenReminderAlert(), _ => HasReminderAlert);
    }

    private void Reload()
    {
        Items.Clear();
        if (_store is null)
        {
            RaiseItemStateChanged();
            return;
        }

        var now = _now().ToLocalTime();
        var today = now.Date;
        var nextWeek = today.AddDays(7);
        IEnumerable<TodoItem> query = _store.Load();
        query = SelectedFilterId switch
        {
            "completed" => query.Where(item => item.Status == TodoStatus.Completed),
            "today" => query.Where(item => item.Status == TodoStatus.Pending)
                .Where(item => item.DueAt?.ToLocalTime().Date == today
                    || item.ReminderAt?.ToLocalTime().Date == today),
            "upcoming" => query.Where(item => item.Status == TodoStatus.Pending)
                .Where(item =>
                {
                    var relevant = item.ReminderAt ?? item.DueAt;
                    return relevant is not null
                        && relevant.Value.ToLocalTime().Date >= today
                        && relevant.Value.ToLocalTime().Date <= nextWeek;
                }),
            _ => query.Where(item => item.Status == TodoStatus.Pending),
        };

        foreach (var item in query
            .OrderBy(item => item.DueAt ?? item.ReminderAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.CreatedAt))
            Items.Add(new TodoRowViewModel(item));
        RaiseItemStateChanged();
    }

    private void OpenNewEditor()
    {
        _editingId = null;
        EditorTitle = string.Empty;
        EditorNotes = string.Empty;
        EditorDueDate = null;
        EditorDueTime = "18:00";
        EditorReminderDate = null;
        EditorReminderTime = "09:00";
        EditorIsReminder = false;
        EditorReminderRoamEnabled = false;
        EditorReminderBubbleEnabled = true;
        EditorError = string.Empty;
        IsEditorOpen = true;
        RaiseEditorStateChanged();
    }

    private void OpenEditor(TodoItem? item)
    {
        if (item is null) return;
        _editingId = item.Id;
        EditorTitle = item.Title;
        EditorNotes = item.Notes;
        EditorDueDate = item.DueAt?.ToLocalTime().DateTime.Date;
        EditorDueTime = item.DueAt?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? "18:00";
        EditorReminderDate = item.ReminderAt?.ToLocalTime().DateTime.Date;
        EditorReminderTime = item.ReminderAt?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? "09:00";
        EditorIsReminder = item.IsReminder;
        EditorReminderRoamEnabled = item.ReminderRoamEnabled;
        EditorReminderBubbleEnabled = item.IsReminder ? item.ReminderBubbleEnabled : true;
        EditorError = string.Empty;
        IsEditorOpen = true;
        RaiseEditorStateChanged();
    }

    private void CloseEditor()
    {
        IsEditorOpen = false;
        EditorError = string.Empty;
    }

    private void SaveEditor()
    {
        if (_store is null) return;
        try
        {
            var dueAt = CombineLocal(EditorDueDate, EditorDueTime, "截止时间");
            var reminderAt = CombineLocal(EditorReminderDate, EditorReminderTime, "提醒时间");
            var now = _now();
            var existing = _editingId is { } id
                ? _store.Load().FirstOrDefault(item => item.Id == id)
                : null;
            if (dueAt is { } due
                && due <= now
                && (existing?.DueAt is null || existing.DueAt != dueAt))
                throw new TodoValidationException("截止时间必须晚于当前时间。");
            if (reminderAt is { } reminder && reminder <= now)
                throw new TodoValidationException("提醒时间必须晚于当前时间。");
            if (EditorIsReminder && reminderAt is null)
                throw new TodoValidationException("提醒项必须设置提醒日期和时间。");

            if (existing is null)
            {
                _store.Create(new TodoItem
                {
                    Title = EditorTitle,
                    Notes = EditorNotes,
                    DueAt = dueAt,
                    ReminderAt = reminderAt,
                    Status = TodoStatus.Pending,
                    ReminderState = reminderAt is null ? ReminderState.None : ReminderState.Scheduled,
                    IsReminder = EditorIsReminder,
                    ReminderRoamEnabled = EditorIsReminder && EditorReminderRoamEnabled,
                    ReminderBubbleEnabled = EditorIsReminder && EditorReminderBubbleEnabled,
                });
                Status = EditorIsReminder ? "提醒项已创建。" : "待办已创建。";
            }
            else
            {
                var reminderState = reminderAt is null
                    ? existing.ReminderAt is null ? existing.ReminderState : ReminderState.Cancelled
                    : existing.ReminderAt == reminderAt
                        ? existing.ReminderState
                        : ReminderState.Scheduled;
                _store.Update(existing with
                {
                    Title = EditorTitle,
                    Notes = EditorNotes,
                    DueAt = dueAt,
                    ReminderAt = reminderAt,
                    ReminderState = reminderState,
                    ReminderFailureCode = reminderAt is null ? null : existing.ReminderFailureCode,
                    IsReminder = EditorIsReminder,
                    ReminderRoamEnabled = EditorIsReminder && EditorReminderRoamEnabled,
                    ReminderBubbleEnabled = EditorIsReminder && EditorReminderBubbleEnabled,
                });
                Status = EditorIsReminder ? "提醒项已更新。" : "待办已更新。";
            }
            CloseEditor();
            Reload();
        }
        catch (TodoValidationException ex)
        {
            EditorError = ex.Message;
        }
        catch
        {
            EditorError = "保存失败，原有数据仍保留，请重试。";
        }
    }

    private async Task RunParseAiAsync()
    {
        var task = ParseAiAsync();
        _aiParseTask = task;
        try { await task; }
        finally
        {
            if (ReferenceEquals(_aiParseTask, task)) _aiParseTask = null;
        }
    }

    public async Task ParseAiAsync()
    {
        if (!CanParseAi() || _todoAiClient is null || _connectionProvider is null) return;
        var connection = _connectionProvider();
        if (connection is null)
        {
            AiError = "尚未保存通过测试的 AI 配置。请先到设置页完成配置，或转为手动填写。";
            AiMessage = "AI 未配置，输入内容已保留。";
            RaiseAiStateChanged();
            return;
        }

        _isAiParsing = true;
        AiError = string.Empty;
        AiClarification = string.Empty;
        _aiDraft = null;
        _aiTarget = null;
        AiTargetChoices.Clear();
        AiMessage = "正在解析当前这句话，不会读取其他本地数据。";
        RaiseAiStateChanged();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _aiParseCts = timeout;
        try
        {
            var result = await _todoAiClient.ParseAsync(
                connection.Endpoint,
                connection.Model,
                connection.ApiKey,
                new AiTodoParseRequest(AiInput, _now(), TimeZoneInfo.Local.DisplayName),
                timeout.Token);
            switch (result.Status)
            {
                case AiTodoParseStatus.NeedsClarification:
                    AiClarification = result.Clarification ?? "请补充影响创建结果的必要信息。";
                    AiMessage = "信息还不够明确，未创建任何待办。";
                    break;
                case AiTodoParseStatus.Failed:
                    AiError = string.Join(" ", new[] { result.ErrorMessage, result.Suggestion }
                        .Where(message => !string.IsNullOrWhiteSpace(message)));
                    AiMessage = "解析失败，输入内容已保留。";
                    break;
                case AiTodoParseStatus.DraftReady:
                    PrepareAiDraft(result.Draft!);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            AiError = "AI 解析超时。请重试，或转为手动填写。";
            AiMessage = "解析超时，输入内容已保留。";
        }
        finally
        {
            if (ReferenceEquals(_aiParseCts, timeout)) _aiParseCts = null;
            _isAiParsing = false;
            RaiseAiStateChanged();
        }
    }

    public void CancelBackgroundWork() => _aiParseCts?.Cancel();

    public async Task WaitForBackgroundWorkAsync(TimeSpan timeout)
    {
        if (_aiParseTask is not { IsCompleted: false } task) return;
        await Task.WhenAny(task, Task.Delay(timeout));
    }

    private void PrepareAiDraft(AiTodoDraft draft)
    {
        _aiDraft = draft;
        if (draft.Operation == AiTodoOperation.Create)
        {
            AiMessage = "草稿已生成。请核对绝对时间后确认创建。";
            RaiseAiStateChanged();
            return;
        }

        var matches = _store?.Load()
            .Where(item => string.Equals(item.Title, draft.TargetTitle, StringComparison.CurrentCultureIgnoreCase))
            .ToArray() ?? Array.Empty<TodoItem>();
        if (matches.Length == 0)
        {
            _aiDraft = null;
            AiClarification = $"没有找到标题为“{draft.TargetTitle}”的待办，请提供准确标题。";
            AiMessage = "没有匹配目标，数据未改变。";
            return;
        }
        if (matches.Length == 1)
        {
            _aiTarget = matches[0];
            AiMessage = "已找到唯一目标。请核对变更前后内容后确认。";
            RaiseAiStateChanged();
            return;
        }

        foreach (var match in matches) AiTargetChoices.Add(new TodoRowViewModel(match));
        _aiTarget = null;
        SelectedAiTarget = null;
        AiMessage = "存在多个同名待办，请先选择唯一目标。";
        RaiseAiStateChanged();
    }

    private void ConfirmAi()
    {
        if (!CanConfirmAi() || _aiDraft is null || _store is null) return;
        try
        {
            switch (_aiDraft.Operation)
            {
                case AiTodoOperation.Create:
                {
                    var createsReminder = AiDraftCreatesReminder;
                    var created = _store.Create(new TodoItem
                    {
                        Title = _aiDraft.Title ?? string.Empty,
                        Notes = _aiDraft.Notes ?? string.Empty,
                        DueAt = _aiDraft.DueAt,
                        ReminderAt = _aiDraft.ReminderAt,
                        Status = TodoStatus.Pending,
                        ReminderState = _aiDraft.ReminderAt is null
                            ? ReminderState.None
                            : ReminderState.Scheduled,
                        IsReminder = createsReminder,
                        ReminderBubbleEnabled = createsReminder,
                    });
                    _undoRecord = new UndoRecord(createsReminder ? "创建提醒项" : "创建待办", created.Id, null);
                    break;
                }
                case AiTodoOperation.Update:
                {
                    var target = RequireAiTarget();
                    var updatedReminder = _aiDraft.ClearReminder
                        ? null
                        : _aiDraft.ReminderAt ?? target.ReminderAt;
                    var reminderState = _aiDraft.ClearReminder
                        ? ReminderState.Cancelled
                        : _aiDraft.ReminderAt is not null
                            ? ReminderState.Scheduled
                            : target.ReminderState;
                    _store.Update(target with
                    {
                        Title = _aiDraft.Title ?? target.Title,
                        Notes = _aiDraft.Notes ?? target.Notes,
                        DueAt = _aiDraft.ClearDue ? null : _aiDraft.DueAt ?? target.DueAt,
                        ReminderAt = updatedReminder,
                        ReminderState = reminderState,
                    });
                    _undoRecord = new UndoRecord("修改待办", target.Id, target);
                    break;
                }
                case AiTodoOperation.Complete:
                {
                    var target = RequireAiTarget();
                    _store.Complete(target.Id);
                    _undoRecord = new UndoRecord("完成待办", target.Id, target);
                    break;
                }
                case AiTodoOperation.Delete:
                {
                    var target = RequireAiTarget();
                    _store.Delete(target.Id);
                    _undoRecord = new UndoRecord("删除待办", target.Id, target);
                    break;
                }
                case AiTodoOperation.Snooze:
                {
                    var target = RequireAiTarget();
                    var next = _aiDraft.ReminderAt
                        ?? _now().AddMinutes(_aiDraft.SnoozeMinutes ?? 10);
                    _store.Snooze(target.Id, next);
                    _undoRecord = new UndoRecord("稍后提醒", target.Id, target);
                    break;
                }
            }

            Status = "已按确认内容执行。";
            ClearAiResult(keepInput: false, keepStatus: true);
            Reload();
            RaiseUndoStateChanged();
        }
        catch (TodoValidationException ex)
        {
            AiError = ex.Message;
            AiMessage = "确认失败，数据未改变。";
        }
        catch
        {
            AiError = "执行失败，原有数据仍保留，请重试。";
            AiMessage = "确认失败，数据未改变。";
        }
        RaiseAiStateChanged();
    }

    private void UseManualFallback()
    {
        OpenNewEditor();
        if (_aiDraft?.Operation == AiTodoOperation.Create)
        {
            EditorTitle = _aiDraft.Title ?? AiInput.Trim();
            EditorNotes = _aiDraft.Notes ?? string.Empty;
            SetEditorDateTime(_aiDraft.DueAt, due: true);
            SetEditorDateTime(_aiDraft.ReminderAt, due: false);
            EditorIsReminder = AiDraftCreatesReminder;
            EditorReminderBubbleEnabled = EditorIsReminder;
        }
        else
        {
            EditorTitle = AiInput.Trim();
        }
        Status = "已转为手动填写，原句仍保留在 AI 输入框。";
    }

    private void ClearAiResult(bool keepInput, bool keepStatus = false)
    {
        if (!keepInput && _aiInput.Length > 0)
        {
            _aiInput = string.Empty;
            OnPropertyChanged(nameof(AiInput));
        }
        _aiDraft = null;
        _aiTarget = null;
        _selectedAiTarget = null;
        AiTargetChoices.Clear();
        AiError = string.Empty;
        AiClarification = string.Empty;
        if (!keepStatus) AiMessage = "AI 只生成草稿；确认前不会创建或修改任何待办。";
        RaiseAiStateChanged();
    }

    private void UndoAi()
    {
        if (_store is null || _undoRecord is null) return;
        try
        {
            if (_undoRecord.Before is null) _store.Delete(_undoRecord.Id);
            else _store.UpsertSnapshot(_undoRecord.Before);
            Status = $"已撤销：{_undoRecord.Label}。";
            _undoRecord = null;
            Reload();
        }
        catch
        {
            Status = "撤销失败，请检查待办是否已被再次修改。";
        }
        RaiseUndoStateChanged();
    }

    private void Complete(TodoItem? item)
    {
        if (item is null || _store is null) return;
        try
        {
            _store.Complete(item.Id);
            if (_reminderAlertItem?.Id == item.Id) DismissReminderAlert();
            Status = "待办已完成；原提醒不会继续触发。";
            Reload();
        }
        catch { Status = "完成状态保存失败，请重试。"; }
    }

    private void Restore(TodoItem? item)
    {
        if (item is null || _store is null) return;
        try
        {
            _store.Restore(item.Id);
            Status = "待办已恢复；已取消或已投递的提醒不会自动恢复。";
            Reload();
        }
        catch { Status = "恢复失败，请重试。"; }
    }

    private void CancelReminder(TodoItem? item)
    {
        if (item is null || _store is null) return;
        try
        {
            _store.CancelReminder(item.Id);
            if (_reminderAlertItem?.Id == item.Id) DismissReminderAlert();
            Status = "仅提醒已取消，待办仍保留。";
            Reload();
        }
        catch { Status = "取消提醒失败，请重试。"; }
    }

    private void Snooze(TodoItem? item)
    {
        if (item is null || _store is null) return;
        try
        {
            _store.Snooze(item.Id, _now().AddMinutes(10));
            if (_reminderAlertItem?.Id == item.Id) DismissReminderAlert();
            Status = "已稍后 10 分钟提醒，完成状态未改变。";
            Reload();
        }
        catch { Status = "稍后提醒保存失败，请重试。"; }
    }

    private void CompleteReminderAlert()
    {
        Complete(_reminderAlertItem);
        DismissReminderAlert();
    }

    private void SnoozeReminderAlert()
    {
        Snooze(_reminderAlertItem);
        DismissReminderAlert();
    }

    private void OpenReminderAlert()
    {
        var item = _reminderAlertItem;
        DismissReminderAlert();
        if (item is null) return;
        SelectedFilterId = item.Status == TodoStatus.Completed ? "completed" : "pending";
        OpenEditor(item);
        Status = "已打开待办详情，完成状态未改变。";
    }

    private void DismissReminderAlert()
    {
        _reminderAlertItem = null;
        ReminderAlertMessage = string.Empty;
        OnPropertyChanged(nameof(HasReminderAlert));
        OnPropertyChanged(nameof(ReminderAlertTitle));
        OnPropertyChanged(nameof(ReminderAlertMessage));
        RaiseReminderCommands();
    }

    private DateTimeOffset? CombineLocal(DateTime? date, string timeText, string fieldName)
    {
        if (date is null) return null;
        if (!TimeOnly.TryParseExact(
            timeText?.Trim(),
            new[] { "H:mm", "HH:mm" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var time))
            throw new TodoValidationException($"{fieldName}需使用 HH:mm 格式。");
        var localDateTime = date.Value.Date.Add(time.ToTimeSpan());
        if (TimeZoneInfo.Local.IsInvalidTime(localDateTime))
            throw new TodoValidationException($"{fieldName}落在夏令时跳过区间，请换一个时间。");
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    private void SetEditorDateTime(DateTimeOffset? value, bool due)
    {
        var local = value?.ToLocalTime();
        if (due)
        {
            EditorDueDate = local?.DateTime.Date;
            EditorDueTime = local?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "18:00";
        }
        else
        {
            EditorReminderDate = local?.DateTime.Date;
            EditorReminderTime = local?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "09:00";
        }
    }

    private TodoItem RequireAiTarget() =>
        _aiTarget ?? throw new TodoValidationException("请先选择唯一待办。 ");

    private TodoItem? Find(object? parameter)
    {
        var id = parameter switch
        {
            Guid guid => guid,
            TodoRowViewModel row => row.Id,
            _ => Guid.Empty,
        };
        return id == Guid.Empty ? null : _store?.Load().FirstOrDefault(item => item.Id == id);
    }

    private bool CanParseAi() =>
        !_isAiParsing
        && _todoAiClient is not null
        && !string.IsNullOrWhiteSpace(AiInput);

    private bool CanConfirmAi() =>
        _aiDraft is not null
        && !_isAiParsing
        && (_aiDraft.Operation == AiTodoOperation.Create || _aiTarget is not null);

    private bool AiDraftCreatesReminder =>
        _aiDraft is { Operation: AiTodoOperation.Create, ReminderAt: not null, DueAt: null };

    private string BuildAiChangeSummary()
    {
        if (_aiDraft is null) return string.Empty;
        if (_aiDraft.Operation == AiTodoOperation.Create)
            return AiDraftCreatesReminder
                ? "确认后新增 1 条提醒项，默认使用桌宠气泡；到期投递后自动完成。"
                : "确认后新增 1 条待办；取消不会写入。";
        if (_aiTarget is null) return "请先选择唯一目标，数据目前未改变。";
        return _aiDraft.Operation switch
        {
            AiTodoOperation.Update => $"变更前：{_aiTarget.Title} · {FormatAbsolute(_aiTarget.DueAt, "无截止")} · {FormatAbsolute(_aiTarget.ReminderAt, "无提醒")}\n变更后：{_aiDraft.Title ?? _aiTarget.Title} · {FormatAbsolute(_aiDraft.ClearDue ? null : _aiDraft.DueAt ?? _aiTarget.DueAt, _aiDraft.ClearDue ? "无截止" : "无截止")} · {FormatAbsolute(_aiDraft.ClearReminder ? null : _aiDraft.ReminderAt ?? _aiTarget.ReminderAt, _aiDraft.ClearReminder ? "无提醒" : "无提醒")}",
            AiTodoOperation.Complete => $"将“{_aiTarget.Title}”标记为已完成，并停止未触发提醒。",
            AiTodoOperation.Delete => $"将永久删除“{_aiTarget.Title}”。",
            AiTodoOperation.Snooze => $"仅把“{_aiTarget.Title}”的下一次提醒改为 {FormatAbsolute(_aiDraft.ReminderAt ?? _now().AddMinutes(_aiDraft.SnoozeMinutes ?? 10), "未提供")}，不自动完成。",
            _ => string.Empty,
        };
    }

    private static string FormatAbsolute(DateTimeOffset? value, string emptyText) =>
        value is { } dateTime
            ? dateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
            : emptyText;

    private void RaiseItemStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(EmptyMessage));
        RaiseAllCommands();
    }

    private void RaiseEditorStateChanged()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorHeading));
        OnPropertyChanged(nameof(EditorSaveLabel));
    }

    private void RaiseAiStateChanged()
    {
        OnPropertyChanged(nameof(IsAiParsing));
        OnPropertyChanged(nameof(AiParseButtonLabel));
        OnPropertyChanged(nameof(HasAiDraft));
        OnPropertyChanged(nameof(NeedsAiTarget));
        OnPropertyChanged(nameof(AiOperationText));
        OnPropertyChanged(nameof(AiDraftTitle));
        OnPropertyChanged(nameof(AiDraftDueText));
        OnPropertyChanged(nameof(AiDraftReminderText));
        OnPropertyChanged(nameof(AiDraftNotes));
        OnPropertyChanged(nameof(AiTimeZoneText));
        OnPropertyChanged(nameof(AiChangeSummary));
        RaiseAiCommands();
    }

    private void RaiseAiCommands()
    {
        (ParseAiCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConfirmAiCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (UseManualCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseUndoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndoAiAction));
        OnPropertyChanged(nameof(UndoLabel));
        (UndoAiCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseReminderCommands()
    {
        (CompleteReminderAlertCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SnoozeReminderAlertCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenReminderAlertCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseAllCommands()
    {
        foreach (var command in new[]
        {
            EditTodoCommand,
            SaveEditorCommand,
            CompleteTodoCommand,
            RestoreTodoCommand,
            CancelReminderCommand,
            SnoozeTodoCommand,
        }.OfType<RelayCommand>()) command.RaiseCanExecuteChanged();
        RaiseAiCommands();
        RaiseUndoStateChanged();
        RaiseReminderCommands();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record UndoRecord(string Label, Guid Id, TodoItem? Before);
}
