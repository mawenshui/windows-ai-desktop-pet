using System.Globalization;
using System.Windows.Input;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed record TodoSortOption(string Id, string DisplayName);

public sealed partial class TodoViewModel
{
    private string _todoListQuery = string.Empty;
    private string _selectedTodoSortId = "time";
    private TodoRowViewModel? _selectedTodo;

    public IReadOnlyList<TodoSortOption> TodoSortOptions { get; } = new[]
    {
        new TodoSortOption("time", "时间最近"),
        new TodoSortOption("newest", "新建在前"),
        new TodoSortOption("title", "按标题"),
    };

    public string TodoListQuery
    {
        get => _todoListQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (_todoListQuery == normalized) return;
            _todoListQuery = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTodoListQuery));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(EmptyActionLabel));
            Reload();
            (ClearTodoListQueryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasTodoListQuery => !string.IsNullOrWhiteSpace(TodoListQuery);

    public string SelectedTodoSortId
    {
        get => _selectedTodoSortId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _selectedTodoSortId == value) return;
            _selectedTodoSortId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTodoSort));
            Reload();
        }
    }

    public TodoSortOption? SelectedTodoSort
    {
        get => TodoSortOptions.FirstOrDefault(option => option.Id == SelectedTodoSortId);
        set
        {
            if (value is not null) SelectedTodoSortId = value.Id;
        }
    }

    public TodoRowViewModel? SelectedTodo
    {
        get => _selectedTodo;
        set
        {
            if (ReferenceEquals(_selectedTodo, value)) return;
            _selectedTodo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTodo));
            OnPropertyChanged(nameof(SelectedTodoSummary));
            OnPropertyChanged(nameof(SelectedCompletionLabel));
            RaiseListExperienceCommands();
        }
    }

    public bool HasSelectedTodo => SelectedTodo is not null;
    public string SelectedCompletionLabel => SelectedTodo?.IsCompleted == true ? "恢复" : "完成";
    public string SelectedTodoSummary
    {
        get
        {
            if (SelectedTodo is null) return string.Empty;
            var index = Items.IndexOf(SelectedTodo);
            var position = index >= 0 ? $"{index + 1}/{Items.Count}" : $"–/{Items.Count}";
            return $"已选 {position}：{SelectedTodo.Title} · {SelectedTodo.ItemTypeText} · {SelectedTodo.UrgencyText} · Enter 编辑";
        }
    }

    public string EditorTitleCountText => $"{EditorTitle.Length}/200";
    public string EditorNotesCountText => $"{EditorNotes.Length}/4000";
    public string EditorValidationHint => GetEditorValidationMessage()
        ?? "内容有效，可按 Ctrl+Enter 保存。";

    public ICommand ClearTodoListQueryCommand { get; private set; } = null!;
    public ICommand EditSelectedTodoCommand { get; private set; } = null!;
    public ICommand ToggleSelectedTodoCompletionCommand { get; private set; } = null!;
    public ICommand SnoozeSelectedTodoCommand { get; private set; } = null!;
    public ICommand SkipSelectedTodoOccurrenceCommand { get; private set; } = null!;
    public ICommand CancelSelectedTodoReminderCommand { get; private set; } = null!;
    public ICommand DeleteSelectedTodoCommand { get; private set; } = null!;
    public ICommand SetDueTodayCommand { get; private set; } = null!;
    public ICommand SetDueTomorrowCommand { get; private set; } = null!;
    public ICommand ClearDueCommand { get; private set; } = null!;
    public ICommand SetReminderInThirtyMinutesCommand { get; private set; } = null!;
    public ICommand SetReminderTomorrowCommand { get; private set; } = null!;
    public ICommand ClearReminderDateCommand { get; private set; } = null!;

    private void InitializeListExperienceCommands()
    {
        ClearTodoListQueryCommand = new RelayCommand(_ => ClearTodoListQuery(), _ => HasTodoListQuery);
        EditSelectedTodoCommand = new RelayCommand(
            _ => OpenEditor(SelectedTodo is { } selected ? Find(selected.Id) : null),
            _ => SelectedTodo is not null);
        ToggleSelectedTodoCompletionCommand = new RelayCommand(
            _ => ToggleSelectedTodoCompletion(),
            _ => SelectedTodo is { IsCompleted: false } || SelectedTodo is { CanRestore: true });
        SnoozeSelectedTodoCommand = new RelayCommand(
            _ => Snooze(SelectedTodo is { } selected ? Find(selected.Id) : null),
            _ => SelectedTodo is { IsCompleted: false });
        SkipSelectedTodoOccurrenceCommand = new RelayCommand(
            _ => SkipOccurrence(SelectedTodo is { } selected ? Find(selected.Id) : null),
            _ => SelectedTodo is { CanSkipOccurrence: true });
        CancelSelectedTodoReminderCommand = new RelayCommand(
            _ => CancelReminder(SelectedTodo is { } selected ? Find(selected.Id) : null),
            _ => SelectedTodo is { CanCancelReminder: true });
        DeleteSelectedTodoCommand = new RelayCommand(
            _ => DeleteSelectedTodo(),
            _ => SelectedTodo is not null);
        SkipOccurrenceCommand = new RelayCommand(
            parameter => SkipOccurrence(Find(parameter)),
            parameter => Find(parameter) is { Status: TodoStatus.Pending } item
                && (item.Recurrence.Kind != RecurrenceKind.None || item.AdditionalReminderTimes.Count > 0)
                && item.ReminderAt is not null);

        SetDueTodayCommand = DraftCommand(_ => SetDueShortcut(_now().ToLocalTime().Date));
        SetDueTomorrowCommand = DraftCommand(_ => SetDueShortcut(_now().ToLocalTime().Date.AddDays(1)));
        ClearDueCommand = DraftCommand(_ =>
        {
            EditorDueDate = null;
            EditorDueTime = "18:00";
        });
        SetReminderInThirtyMinutesCommand = DraftCommand(_ =>
        {
            var target = _now().ToLocalTime().AddMinutes(30);
            if (target.Second > 0 || target.Millisecond > 0) target = target.AddMinutes(1);
            target = new DateTimeOffset(target.Year, target.Month, target.Day, target.Hour, target.Minute, 0, target.Offset);
            EditorReminderDate = target.Date;
            EditorReminderTime = target.ToString("HH:mm", CultureInfo.InvariantCulture);
        });
        SetReminderTomorrowCommand = DraftCommand(_ =>
        {
            EditorReminderDate = _now().ToLocalTime().Date.AddDays(1);
            EditorReminderTime = "09:00";
        });
        ClearReminderDateCommand = DraftCommand(_ =>
        {
            EditorReminderDate = null;
            EditorReminderTime = "09:00";
        });
    }

    private RelayCommand DraftCommand(Action<object?> action) =>
        new(action, _ => IsEditorOpen);

    private void ClearTodoListQuery()
    {
        if (!HasTodoListQuery) return;
        TodoListQuery = string.Empty;
        Status = "已清空待办查找，日期筛选和排序保持不变。";
    }

    private void ToggleSelectedTodoCompletion()
    {
        var current = SelectedTodo is { } selected ? Find(selected.Id) : null;
        if (current is { Status: TodoStatus.Completed, IsReminder: false })
            Restore(current);
        else if (current is { Status: TodoStatus.Pending })
            Complete(current);
    }

    private void DeleteSelectedTodo()
    {
        if (SelectedTodo is { } selected) DeleteTodo(selected.Id);
    }

    private void SetDueShortcut(DateTime date)
    {
        EditorDueDate = date;
        EditorDueTime = "18:00";
    }

    private bool IsEditorDraftValid() => GetEditorValidationMessage() is null;

    private string? GetEditorValidationMessage()
    {
        if (EditorOnlyThis && _editingId is not null)
        {
            if (!IsClockText(EditorReminderTime))
                return "提醒时间请使用 24 小时 HH:mm。";
            if (EditorIsReminder && EditorReminderDate is null)
                return "提醒项必须设置提醒日期和时间。";
            try
            {
                var reminder = CombineReminder(EditorReminderDate, EditorReminderTime);
                if (reminder is { } first && first <= _now()) return "提醒时间必须晚于当前时间。";
            }
            catch (TodoValidationException ex)
            {
                return ex.Message;
            }
            return null;
        }

        if (string.IsNullOrWhiteSpace(EditorTitle)) return "填写标题后即可保存。";
        if (EditorTitle.Length > 200) return "标题不能超过 200 个字符。";
        if (EditorNotes.Length > 4000) return "备注不能超过 4000 个字符。";
        if (!IsClockText(EditorDueTime))
            return "截止时间请使用 24 小时 HH:mm。";
        if (!IsClockText(EditorReminderTime))
            return "提醒时间请使用 24 小时 HH:mm。";
        if (EditorIsReminder && EditorReminderDate is null)
            return "提醒项必须设置提醒日期和时间。";
        if (EditorInterval is < 1 or > 365) return "重复间隔请输入 1 到 365。";
        if (EditorRecurrence?.Kind == RecurrenceKind.CustomDays && !Weekdays.Any(day => day.IsSelected))
            return "自选星期至少选择一天。";
        if (!TimeZones.Contains(EditorTimeZoneId, StringComparer.Ordinal)) return "请选择有效时区。";

        try
        {
            var due = CombineLocal(EditorDueDate, EditorDueTime, "截止时间");
            var reminder = CombineReminder(EditorReminderDate, EditorReminderTime);
            var rule = ReadEditorRule();
            var additional = ReadAdditionalTimes();
            var existing = _editingId is { } id ? _store?.Load().FirstOrDefault(item => item.Id == id) : null;
            if (due is { } deadline
                && deadline <= _now()
                && (existing?.DueAt is null || existing.DueAt != deadline))
                return "截止时间必须晚于当前时间。";
            if (rule.Kind != RecurrenceKind.None && reminder is null)
                return "重复规则必须设置首次提醒时间。";
            if (reminder is { } first && first <= _now()) return "提醒时间必须晚于当前时间。";
            if (additional.Any(time => time <= _now())) return "所有额外提醒时间都必须晚于当前时间。";
        }
        catch (TodoValidationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    private static bool IsClockText(string value) => TimeOnly.TryParseExact(
        value?.Trim(),
        new[] { "H:mm", "HH:mm" },
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out _);

    private static bool MatchesTodoFilter(
        TodoItem item,
        string filterId,
        DateTimeOffset now,
        DateTime today,
        DateTime nextWeek) => filterId switch
    {
        "completed" => item.Status == TodoStatus.Completed,
        "today" => item.Status == TodoStatus.Pending
            && (item.PlannedStartAt?.ToLocalTime().Date == today
                || item.DueAt?.ToLocalTime().Date == today
                || item.ReminderAt?.ToLocalTime().Date == today),
        "overdue" => item.Status == TodoStatus.Pending
            && (item.DueAt is { } due && due < now
                || item.ReminderAt is { } reminder && reminder < now),
        "unscheduled" => item.Status == TodoStatus.Pending
            && item.DueAt is null
            && item.ReminderAt is null
            && item.PlannedStartAt is null,
        "upcoming" => item.Status == TodoStatus.Pending && IsInUpcomingWindow(item, today, nextWeek),
        _ => item.Status == TodoStatus.Pending,
    };

    private static bool IsInUpcomingWindow(TodoItem item, DateTime today, DateTime nextWeek)
    {
        var relevant = item.ReminderAt ?? item.DueAt;
        return relevant is not null
            && relevant.Value.ToLocalTime().Date >= today
            && relevant.Value.ToLocalTime().Date <= nextWeek;
    }

    private IEnumerable<TodoItem> ApplyTodoSort(IEnumerable<TodoItem> items) => SelectedTodoSortId switch
    {
        "newest" => items
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
        "title" => items
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.CreatedAt),
        _ => items
            .OrderBy(item => item.DueAt ?? item.ReminderAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.CreatedAt),
    };

    private void RaiseListExperienceCommands()
    {
        foreach (var command in new[]
        {
            ClearTodoListQueryCommand,
            EditSelectedTodoCommand,
            ToggleSelectedTodoCompletionCommand,
            SnoozeSelectedTodoCommand,
            SkipSelectedTodoOccurrenceCommand,
            CancelSelectedTodoReminderCommand,
            DeleteSelectedTodoCommand,
            SetDueTodayCommand,
            SetDueTomorrowCommand,
            ClearDueCommand,
            SetReminderInThirtyMinutesCommand,
            SetReminderTomorrowCommand,
            ClearReminderDateCommand,
            SkipOccurrenceCommand,
        }.OfType<RelayCommand>()) command.RaiseCanExecuteChanged();
    }
}
