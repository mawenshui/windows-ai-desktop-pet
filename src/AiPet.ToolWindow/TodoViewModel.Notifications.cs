using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed class NotificationFilterOption : INotifyPropertyChanged
{
    private int _count;

    public NotificationFilterOption(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public int Count
    {
        get => _count;
        internal set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new(nameof(Count)));
            PropertyChanged?.Invoke(this, new(nameof(DisplayText)));
        }
    }

    public string DisplayText => $"{DisplayName} {Count}";
    public override string ToString() => DisplayName;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record NotificationSortOption(string Id, string DisplayName);

public sealed class NotificationRowViewModel
{
    public NotificationRowViewModel(NotificationEntry entry) => Entry = entry;

    public NotificationEntry Entry { get; }
    public Guid Id => Entry.Id;
    public Guid TodoId => Entry.TodoId;
    public string Title => Entry.Title;
    public string TimeText => Entry.TimeText;
    public string StateText => Entry.StateText;
    public bool CanHandle => Entry.CanHandle;
    public bool CanOpen => TodoId != Guid.Empty;
    public bool IsRecovery => Entry.IsRecovery;
    public string TypeText => TodoId == Guid.Empty
        ? "通道测试"
        : Entry.IsReminder ? "独立提醒" : "待办提醒";
    public string RecoveryText => IsRecovery ? "启动后恢复" : string.Empty;
    public string AutomationName => IsRecovery
        ? $"{Title}，{TypeText}，{StateText}，计划 {TimeText}，启动后恢复"
        : $"{Title}，{TypeText}，{StateText}，计划 {TimeText}";
}

public sealed partial class TodoViewModel
{
    private sealed record QuietDraftSnapshot(bool Enabled, string Start, string End);

    private NotificationCenter? _notifications;
    private IReadOnlyList<NotificationEntry> _notificationSnapshot = Array.Empty<NotificationEntry>();
    private string _notificationQuery = string.Empty;
    private string _selectedNotificationFilterId = "pending";
    private string _selectedNotificationSortId = "priority";
    private NotificationRowViewModel? _selectedNotification;
    private int _pendingNotificationCount;
    private int _historyNotificationCount;
    private int _clearableNotificationCount;
    private int _snoozeMinutes = 10;
    private bool _quietEnabled;
    private string _quietStart = "22:00";
    private string _quietEnd = "08:00";
    private QuietDraftSnapshot? _quietBaseline;
    private bool _loadingQuietSettings;
    private string _notificationStatus = "提醒记录只保存在当前 Windows 账户。";

    public ObservableCollection<NotificationRowViewModel> NotificationHistory { get; } = new();
    public IReadOnlyList<int> SnoozeOptions { get; } = new[] { 5, 10, 30, 60 };
    public IReadOnlyList<NotificationFilterOption> NotificationFilters { get; } = new[]
    {
        new NotificationFilterOption("pending", "待处理"),
        new NotificationFilterOption("history", "历史"),
        new NotificationFilterOption("all", "全部"),
    };
    public IReadOnlyList<NotificationSortOption> NotificationSortOptions { get; } = new[]
    {
        new NotificationSortOption("priority", "处理优先"),
        new NotificationSortOption("recent", "最近记录"),
    };

    public string NotificationQuery
    {
        get => _notificationQuery;
        set
        {
            var normalized = value ?? string.Empty;
            if (_notificationQuery == normalized) return;
            _notificationQuery = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNotificationQuery));
            RefreshNotificationProjection();
            RaiseNotificationCommands();
        }
    }

    public bool HasNotificationQuery => !string.IsNullOrWhiteSpace(NotificationQuery);

    public string SelectedNotificationFilterId
    {
        get => _selectedNotificationFilterId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _selectedNotificationFilterId == value) return;
            _selectedNotificationFilterId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNotificationFilter));
            RefreshNotificationProjection();
        }
    }

    public NotificationFilterOption? SelectedNotificationFilter
    {
        get => NotificationFilters.FirstOrDefault(option => option.Id == SelectedNotificationFilterId);
        set
        {
            if (value is not null) SelectedNotificationFilterId = value.Id;
        }
    }

    public string SelectedNotificationSortId
    {
        get => _selectedNotificationSortId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _selectedNotificationSortId == value) return;
            _selectedNotificationSortId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNotificationSort));
            RefreshNotificationProjection();
        }
    }

    public NotificationSortOption? SelectedNotificationSort
    {
        get => NotificationSortOptions.FirstOrDefault(option => option.Id == SelectedNotificationSortId);
        set
        {
            if (value is not null) SelectedNotificationSortId = value.Id;
        }
    }

    public NotificationRowViewModel? SelectedNotification
    {
        get => _selectedNotification;
        set
        {
            if (ReferenceEquals(_selectedNotification, value)) return;
            _selectedNotification = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedNotification));
            OnPropertyChanged(nameof(SelectedNotificationSummary));
            OnPropertyChanged(nameof(CanOpenSelectedNotification));
            OnPropertyChanged(nameof(CanActOnSelectedNotification));
            OnPropertyChanged(nameof(SelectedNotificationOpenAutomationName));
            OnPropertyChanged(nameof(SelectedNotificationCompleteAutomationName));
            OnPropertyChanged(nameof(SelectedNotificationSnoozeAutomationName));
            RaiseNotificationCommands();
        }
    }

    public bool HasSelectedNotification => SelectedNotification is not null;
    public bool CanOpenSelectedNotification => SelectedNotification?.CanOpen == true;
    public bool CanActOnSelectedNotification => SelectedNotification is { CanHandle: true, CanOpen: true };
    public bool HasVisibleNotifications => NotificationHistory.Count > 0;
    public bool HasNoVisibleNotifications => !HasVisibleNotifications;
    public int ClearableNotificationCount => _clearableNotificationCount;
    public string ClearNotificationHistoryLabel => $"清除 {ClearableNotificationCount} 条已处理/测试记录";
    public string NotificationSummary => $"提醒中心 · {_pendingNotificationCount} 条待处理 · {_historyNotificationCount} 条历史";
    public string NotificationFilterSummary => HasNotificationQuery
        ? $"当前：{SelectedNotificationFilter?.DisplayName ?? "待处理"} 匹配 {NotificationHistory.Count} 条 · 标题查找不影响分段计数"
        : $"当前：{SelectedNotificationFilter?.DisplayName ?? "待处理"} {NotificationHistory.Count} 条 · 共 {_notificationSnapshot.Count} 条记录";
    public string NotificationEmptyMessage
    {
        get
        {
            if (HasNotificationQuery) return $"当前分段没有标题匹配“{NotificationQuery.Trim()}”的提醒。";
            if (_notificationSnapshot.Count == 0) return "还没有提醒记录。可以主动测试一次本机提醒通道。";
            return SelectedNotificationFilterId switch
            {
                "history" => "还没有已处理或已取消的提醒历史。",
                "all" => "当前没有可显示的提醒记录。",
                _ => "当前没有待处理提醒。",
            };
        }
    }
    public string NotificationEmptyActionLabel
    {
        get
        {
            if (HasNotificationQuery) return "清空提醒查找";
            if (_notificationSnapshot.Count == 0) return "测试提醒通道";
            if (SelectedNotificationFilterId == "pending" && _historyNotificationCount > 0) return "查看提醒历史";
            return "返回待处理";
        }
    }
    public string SelectedNotificationSummary
    {
        get
        {
            if (SelectedNotification is null) return string.Empty;
            var index = NotificationHistory.IndexOf(SelectedNotification);
            var position = index >= 0 ? $"{index + 1}/{NotificationHistory.Count}" : $"–/{NotificationHistory.Count}";
            return $"已选 {position}：{SelectedNotification.Title} · {SelectedNotification.TypeText} · {SelectedNotification.StateText} · {SelectedNotification.TimeText}";
        }
    }
    public string SelectedNotificationOpenAutomationName => SelectedNotification is null
        ? "打开所选提醒详情"
        : $"打开提醒详情：{SelectedNotification.Title}";
    public string SelectedNotificationCompleteAutomationName => SelectedNotification is null
        ? "完成所选提醒事项"
        : $"完成提醒事项：{SelectedNotification.Title}";
    public string SelectedNotificationSnoozeAutomationName => SelectedNotification is null
        ? "稍后处理所选提醒"
        : $"将提醒推迟 {SnoozeMinutes} 分钟：{SelectedNotification.Title}";

    public int SnoozeMinutes
    {
        get => _snoozeMinutes;
        set
        {
            if (_snoozeMinutes == value) return;
            _snoozeMinutes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNotificationSnoozeAutomationName));
        }
    }

    public bool QuietEnabled
    {
        get => _quietEnabled;
        set
        {
            if (_quietEnabled == value) return;
            _quietEnabled = value;
            OnPropertyChanged();
            OnQuietDraftChanged();
        }
    }

    public string QuietStart
    {
        get => _quietStart;
        set
        {
            var normalized = value ?? string.Empty;
            if (_quietStart == normalized) return;
            _quietStart = normalized;
            OnPropertyChanged();
            OnQuietDraftChanged();
        }
    }

    public string QuietEnd
    {
        get => _quietEnd;
        set
        {
            var normalized = value ?? string.Empty;
            if (_quietEnd == normalized) return;
            _quietEnd = normalized;
            OnPropertyChanged();
            OnQuietDraftChanged();
        }
    }

    public bool HasUnsavedQuietChanges => _quietBaseline is not null
        && _quietBaseline != CaptureQuietDraft();
    public bool CanSaveQuietSettings => _notifications is not null
        && HasUnsavedQuietChanges
        && GetQuietValidationMessage() is null;
    public string QuietSettingsSaveLabel => HasUnsavedQuietChanges
        ? "保存静默设置"
        : "静默设置已保存";
    public string QuietValidationHint => GetQuietValidationMessage()
        ?? (HasUnsavedQuietChanges
            ? "内容有效，保存后生效。"
            : QuietEnabled
                ? $"静默已保存：{QuietStart}–{QuietEnd}。"
                : "静默已关闭并保存。");

    public string NotificationStatus
    {
        get => _notificationStatus;
        private set
        {
            if (_notificationStatus == value) return;
            _notificationStatus = value;
            OnPropertyChanged();
        }
    }

    public ICommand ClearNotificationQueryCommand { get; private set; } = null!;
    public ICommand NotificationEmptyStateCommand { get; private set; } = null!;
    public ICommand SaveQuietHoursCommand { get; private set; } = null!;
    public ICommand ApplyQuietPresetCommand { get; private set; } = null!;
    public ICommand ClearNotificationHistoryCommand { get; private set; } = null!;
    public ICommand TestNotificationCommand { get; private set; } = null!;
    public ICommand CompleteNotificationCommand { get; private set; } = null!;
    public ICommand SnoozeNotificationCommand { get; private set; } = null!;
    public ICommand OpenNotificationCommand { get; private set; } = null!;
    public ICommand CompleteSelectedNotificationCommand { get; private set; } = null!;
    public ICommand SnoozeSelectedNotificationCommand { get; private set; } = null!;
    public ICommand OpenSelectedNotificationCommand { get; private set; } = null!;

    private void InitializeNotificationCommands()
    {
        ClearNotificationQueryCommand = new RelayCommand(
            _ => ClearNotificationQuery(),
            _ => HasNotificationQuery);
        NotificationEmptyStateCommand = new RelayCommand(_ => ActivateNotificationEmptyState());
        SaveQuietHoursCommand = new RelayCommand(
            _ => SaveQuietHours(),
            _ => CanSaveQuietSettings);
        ApplyQuietPresetCommand = new RelayCommand(parameter => ApplyQuietPreset(parameter as string));
        ClearNotificationHistoryCommand = new RelayCommand(
            _ => ClearNotificationHistory(),
            _ => ClearableNotificationCount > 0);
        TestNotificationCommand = new RelayCommand(
            _ => TestNotificationChannel(),
            _ => _notifications is not null);
        CompleteNotificationCommand = new RelayCommand(
            parameter => ActOnNotification(parameter, "complete"),
            CanActOnNotificationParameter);
        SnoozeNotificationCommand = new RelayCommand(
            parameter => ActOnNotification(parameter, "snooze"),
            CanActOnNotificationParameter);
        OpenNotificationCommand = new RelayCommand(
            parameter => ActOnNotification(parameter, "open"),
            CanOpenNotificationParameter);
        CompleteSelectedNotificationCommand = new RelayCommand(
            _ => ActOnNotification(SelectedNotification?.Id, "complete"),
            _ => CanActOnSelectedNotification);
        SnoozeSelectedNotificationCommand = new RelayCommand(
            _ => ActOnNotification(SelectedNotification?.Id, "snooze"),
            _ => CanActOnSelectedNotification);
        OpenSelectedNotificationCommand = new RelayCommand(
            _ => ActOnNotification(SelectedNotification?.Id, "open"),
            _ => CanOpenSelectedNotification);
    }

    public void AttachNotificationCenter(NotificationCenter center)
    {
        _notifications = center;
        try
        {
            var quiet = center.Quiet;
            _loadingQuietSettings = true;
            QuietEnabled = quiet.Enabled;
            QuietStart = quiet.Start;
            QuietEnd = quiet.End;
            _loadingQuietSettings = false;
            _quietBaseline = CaptureQuietDraft();
            RefreshNotifications();
            SetNotificationStatus("提醒中心已就绪；查询、筛选和排序只在本次运行中保留。");
        }
        catch
        {
            _loadingQuietSettings = false;
            SetNotificationStatus("提醒记录无法读取，请保留本地数据并检查备份。");
        }

        RaiseQuietProperties();
        RaiseNotificationCommands();
    }

    public void RefreshNotifications()
    {
        if (_notifications is null) return;
        _notificationSnapshot = _notifications.Entries;
        RefreshNotificationProjection();
    }

    private void RefreshNotificationProjection()
    {
        var selectedId = SelectedNotification?.Id;
        _pendingNotificationCount = _notificationSnapshot.Count(IsPendingNotification);
        _historyNotificationCount = _notificationSnapshot.Count(entry => entry.State is NotificationDeliveryState.Handled or NotificationDeliveryState.Cancelled);
        _clearableNotificationCount = _notificationSnapshot.Count(entry =>
            entry.State is NotificationDeliveryState.Handled or NotificationDeliveryState.Cancelled
            || entry.State == NotificationDeliveryState.Submitted && entry.TodoId == Guid.Empty);

        foreach (var filter in NotificationFilters)
        {
            filter.Count = filter.Id switch
            {
                "pending" => _pendingNotificationCount,
                "history" => _historyNotificationCount,
                _ => _notificationSnapshot.Count,
            };
        }

        var query = NotificationQuery.Trim();
        var projected = _notificationSnapshot
            .Where(MatchesNotificationFilter)
            .Where(entry => query.Length == 0 || entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        projected = SelectedNotificationSortId == "recent"
            ? projected.OrderByDescending(entry => entry.QueuedAt).ThenBy(entry => entry.Id)
            : projected.OrderBy(entry => IsPendingNotification(entry) ? 0 : 1)
                .ThenBy(entry => IsPendingNotification(entry) ? entry.ScheduledAt : DateTimeOffset.MaxValue)
                .ThenByDescending(entry => entry.QueuedAt)
                .ThenBy(entry => entry.Id);

        var entries = projected.ToArray();
        if (!NotificationHistory.Select(row => row.Entry).SequenceEqual(entries))
        {
            NotificationHistory.Clear();
            foreach (var entry in entries) NotificationHistory.Add(new(entry));
        }

        SelectedNotification = selectedId is { } id
            ? NotificationHistory.FirstOrDefault(row => row.Id == id)
            : null;

        OnPropertyChanged(nameof(HasVisibleNotifications));
        OnPropertyChanged(nameof(HasNoVisibleNotifications));
        OnPropertyChanged(nameof(NotificationSummary));
        OnPropertyChanged(nameof(NotificationFilterSummary));
        OnPropertyChanged(nameof(NotificationEmptyMessage));
        OnPropertyChanged(nameof(NotificationEmptyActionLabel));
        OnPropertyChanged(nameof(ClearableNotificationCount));
        OnPropertyChanged(nameof(ClearNotificationHistoryLabel));
        OnPropertyChanged(nameof(SelectedNotificationSummary));
        RaiseNotificationCommands();
    }

    private bool MatchesNotificationFilter(NotificationEntry entry) => SelectedNotificationFilterId switch
    {
        "history" => entry.State is NotificationDeliveryState.Handled or NotificationDeliveryState.Cancelled,
        "all" => true,
        _ => IsPendingNotification(entry),
    };

    private static bool IsPendingNotification(NotificationEntry entry) =>
        entry.State is NotificationDeliveryState.Queued or NotificationDeliveryState.Submitted;

    private bool CanActOnNotificationParameter(object? parameter) => parameter is Guid id
        && _notificationSnapshot.FirstOrDefault(entry => entry.Id == id) is { CanHandle: true, TodoId: var todoId }
        && todoId != Guid.Empty;

    private bool CanOpenNotificationParameter(object? parameter) => parameter is Guid id
        && _notificationSnapshot.FirstOrDefault(entry => entry.Id == id) is { TodoId: var todoId }
        && todoId != Guid.Empty;

    private void ClearNotificationQuery()
    {
        if (!HasNotificationQuery) return;
        NotificationQuery = string.Empty;
        SetNotificationStatus("已清空提醒查找，分段和排序保持不变。");
    }

    private void ActivateNotificationEmptyState()
    {
        if (HasNotificationQuery)
        {
            ClearNotificationQuery();
            return;
        }

        if (_notificationSnapshot.Count == 0)
        {
            TestNotificationChannel();
            return;
        }

        SelectedNotificationFilterId = SelectedNotificationFilterId == "pending" && _historyNotificationCount > 0
            ? "history"
            : "pending";
        SetNotificationStatus(SelectedNotificationFilterId == "history"
            ? "已切换到提醒历史。"
            : "已返回待处理提醒。");
    }

    private void SaveQuietHours()
    {
        if (_notifications is null) return;
        var validation = GetQuietValidationMessage();
        if (validation is not null)
        {
            SetNotificationStatus(validation);
            return;
        }

        try
        {
            var normalizedStart = QuietStart.Trim();
            var normalizedEnd = QuietEnd.Trim();
            _notifications.SetQuietHours(new(QuietEnabled, normalizedStart, normalizedEnd));
            _loadingQuietSettings = true;
            QuietStart = normalizedStart;
            QuietEnd = normalizedEnd;
            _loadingQuietSettings = false;
            _quietBaseline = CaptureQuietDraft();
            RaiseQuietProperties();
            SetNotificationStatus(QuietEnabled
                ? $"静默时段已保存：{QuietStart}–{QuietEnd}；期间提醒排队，结束后合并提交。"
                : "静默时段已关闭并保存。");
        }
        catch (TodoValidationException ex)
        {
            SetNotificationStatus(ex.Message);
        }
        catch
        {
            SetNotificationStatus("静默设置未保存，输入已保留，请重试。");
        }
    }

    private void ApplyQuietPreset(string? preset)
    {
        _loadingQuietSettings = true;
        switch (preset)
        {
            case "night":
                QuietEnabled = true;
                QuietStart = "22:00";
                QuietEnd = "08:00";
                break;
            case "lunch":
                QuietEnabled = true;
                QuietStart = "12:00";
                QuietEnd = "13:00";
                break;
            case "off":
                QuietEnabled = false;
                break;
            default:
                _loadingQuietSettings = false;
                return;
        }

        _loadingQuietSettings = false;
        OnQuietDraftChanged();
        SetNotificationStatus(preset == "off"
            ? "已在草稿中关闭静默，保存后生效。"
            : "已应用静默时段预设，保存后生效。");
    }

    private void ClearNotificationHistory()
    {
        if (_notifications is null || ClearableNotificationCount == 0) return;
        var count = ClearableNotificationCount;
        try
        {
            _notifications.ClearHistory();
            RefreshNotifications();
            SetNotificationStatus($"已清除 {count} 条处理或取消历史，未处理提醒与待办保留。");
        }
        catch
        {
            SetNotificationStatus("提醒历史未清除，现有记录已保留，请重试。");
        }
    }

    private void TestNotificationChannel()
    {
        try
        {
            _notifications?.EnqueueChannelTest();
            RefreshNotifications();
            SetNotificationStatus("通道测试已排队；静默时段结束后提交。");
        }
        catch
        {
            SetNotificationStatus("测试未能排队，请检查本地存储。");
        }
    }

    private void ActOnNotification(object? parameter, string action)
    {
        if (parameter is not Guid id || _notifications is null || _store is null) return;
        try
        {
            var entry = _notifications.Entries.FirstOrDefault(candidate => candidate.Id == id);
            if (entry is null) return;
            var item = _store.Load().FirstOrDefault(candidate => candidate.Id == entry.TodoId);
            if (item is null)
            {
                _notifications.Handle(id);
                RefreshNotifications();
                SetNotificationStatus("对应事项已删除，提醒记录已标记为处理。");
                return;
            }

            if (action == "open")
            {
                SelectedFilterId = item.Status == TodoStatus.Completed ? "completed" : "pending";
                OpenEditor(item);
                SetNotificationStatus($"已打开提醒事项：{item.Title}。");
                return;
            }

            if (!entry.CanHandle) return;
            if (action == "complete")
            {
                _store.Complete(item.Id);
            }
            else
            {
                if (!SnoozeOptions.Contains(SnoozeMinutes))
                    throw new TodoValidationException("请选择有效的稍后时长。");
                _store.Snooze(item.Id, _now().AddMinutes(SnoozeMinutes));
            }

            _notifications.CancelForTodo(item.Id);
            _notifications.Handle(id);
            RefreshNotifications();
            Reload();
            SetNotificationStatus(action == "complete"
                ? $"已完成提醒事项：{item.Title}。"
                : $"已将“{item.Title}”安排在 {SnoozeMinutes} 分钟后提醒。");
        }
        catch (TodoValidationException ex)
        {
            SetNotificationStatus(ex.Message);
        }
        catch
        {
            SetNotificationStatus("提醒操作未完成，现有数据已保留，请重试。");
        }
    }

    private QuietDraftSnapshot CaptureQuietDraft() => new(QuietEnabled, QuietStart, QuietEnd);

    private string? GetQuietValidationMessage()
    {
        if (!TimeOnly.TryParseExact(QuietStart.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return "静默开始时间请使用 24 小时 HH:mm。";
        if (!TimeOnly.TryParseExact(QuietEnd.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return "静默结束时间请使用 24 小时 HH:mm。";
        if (QuietEnabled && start == end)
            return "启用静默时，开始和结束时间不能相同。";
        return null;
    }

    private void OnQuietDraftChanged()
    {
        if (_loadingQuietSettings) return;
        RaiseQuietProperties();
        SetNotificationStatus(GetQuietValidationMessage()
            ?? (HasUnsavedQuietChanges ? "静默设置有未保存修改。" : "静默设置与已保存内容一致。"));
    }

    private void RaiseQuietProperties()
    {
        OnPropertyChanged(nameof(HasUnsavedQuietChanges));
        OnPropertyChanged(nameof(CanSaveQuietSettings));
        OnPropertyChanged(nameof(QuietSettingsSaveLabel));
        OnPropertyChanged(nameof(QuietValidationHint));
        RaiseNotificationCommands();
    }

    private void SetNotificationStatus(string message)
    {
        NotificationStatus = message;
        Status = message;
    }

    private void RaiseNotificationCommands()
    {
        foreach (var command in new[]
        {
            ClearNotificationQueryCommand,
            NotificationEmptyStateCommand,
            SaveQuietHoursCommand,
            ApplyQuietPresetCommand,
            ClearNotificationHistoryCommand,
            TestNotificationCommand,
            CompleteNotificationCommand,
            SnoozeNotificationCommand,
            OpenNotificationCommand,
            CompleteSelectedNotificationCommand,
            SnoozeSelectedNotificationCommand,
            OpenSelectedNotificationCommand,
        }.OfType<RelayCommand>()) command.RaiseCanExecuteChanged();
    }
}
