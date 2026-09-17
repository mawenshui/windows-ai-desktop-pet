using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed partial class TodoViewModel
{
    private FocusSessionService? _focusService;
    private DispatcherTimer? _focusTimer;
    private FocusSessionSnapshot _focusSnapshot = new(null, 0, 0, 0);
    private Guid? _focusTodoId;
    private string _focusTodoTitle = string.Empty;
    private string _focusDurationMinutes = "25";
    private bool _showFocusEndConfirmation;
    private bool _showFocusResult;
    private string _focusMessage = "选择时长即可开始；关联待办是可选的。";

    public event Action<FocusSessionSnapshot>? FocusStateChanged;
    public event Action<FocusSessionSnapshot>? FocusCompleted;

    public FocusSessionSnapshot CurrentFocus => _focusSnapshot;
    public bool IsFocusIdle => !_focusSnapshot.IsActive;
    public bool IsFocusRunning => _focusSnapshot.IsRunning;
    public bool IsFocusPaused => _focusSnapshot.IsPaused;
    public bool IsFocusActive => _focusSnapshot.IsActive;
    public bool ShowFocusEndConfirmation => _showFocusEndConfirmation;
    public bool ShowFocusResult => _showFocusResult;
    public string FocusDurationMinutes
    {
        get => _focusDurationMinutes;
        set
        {
            value ??= string.Empty;
            if (_focusDurationMinutes == value) return;
            _focusDurationMinutes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FocusDurationHint));
            RaiseFocusCommands();
        }
    }
    public string FocusDurationHint => TryReadFocusDuration(out _) ? "可开始专注" : "请输入 5 到 180 分钟";
    public string FocusAssociationText => _focusTodoId is null ? "未关联待办" : $"关联：{_focusTodoTitle}";
    public string FocusSelectedTodoHint => SelectedTodo switch
    {
        null => "先在清单中选中一项，可将本次专注关联到它。",
        { IsCompleted: true } => "已完成待办不能作为新的专注目标。",
        { IsReminder: true } => "独立提醒不能作为专注目标。",
        _ => $"当前选中：{SelectedTodo.Title}",
    };
    public string FocusRemainingText => FormatClock(_focusSnapshot.RemainingSeconds);
    public string FocusElapsedText => $"已专注 {FormatClock(_focusSnapshot.ElapsedSeconds)}";
    public string FocusStateText => !IsFocusActive && !ShowFocusResult ? "准备开始" : _focusSnapshot.Session?.Status switch
    {
        FocusSessionStatus.Running => "专注中",
        FocusSessionStatus.Paused => "已暂停",
        FocusSessionStatus.Completed => "本次专注已完成",
        FocusSessionStatus.EndedEarly => "本次专注已提前结束",
        _ => "准备开始",
    };
    public string FocusMessage
    {
        get => _focusMessage;
        private set { if (_focusMessage == value) return; _focusMessage = value; OnPropertyChanged(); }
    }
    public string FocusResultText => _focusSnapshot.Session is null
        ? string.Empty
        : $"本次记录 {FormatFocusDuration(_focusSnapshot.ElapsedSeconds)}" + (_focusTodoId is null ? string.Empty : $" · {_focusTodoTitle}");
    public bool CanCompleteFocusTodo => _focusSnapshot.Session?.TodoId is { } id
        && _store?.Load().FirstOrDefault(item => item.Id == id) is { Status: TodoStatus.Pending };

    public ICommand SetFocusDurationCommand { get; private set; } = null!;
    public ICommand UseSelectedTodoForFocusCommand { get; private set; } = null!;
    public ICommand ClearFocusTodoCommand { get; private set; } = null!;
    public ICommand StartFocusCommand { get; private set; } = null!;
    public ICommand PauseFocusCommand { get; private set; } = null!;
    public ICommand ResumeFocusCommand { get; private set; } = null!;
    public ICommand RequestEndFocusCommand { get; private set; } = null!;
    public ICommand ConfirmEndFocusCommand { get; private set; } = null!;
    public ICommand CancelEndFocusCommand { get; private set; } = null!;
    public ICommand CompleteFocusTodoCommand { get; private set; } = null!;
    public ICommand CloseFocusResultCommand { get; private set; } = null!;

    private void InitializeFocusCommands()
    {
        SetFocusDurationCommand = new RelayCommand(parameter =>
        {
            if (parameter is string text) FocusDurationMinutes = text;
        });
        UseSelectedTodoForFocusCommand = new RelayCommand(_ => UseSelectedTodoForFocus(), _ => IsFocusIdle && SelectedTodo is { IsCompleted: false, IsReminder: false });
        ClearFocusTodoCommand = new RelayCommand(_ => ClearFocusAssociation(), _ => IsFocusIdle && _focusTodoId is not null);
        StartFocusCommand = new RelayCommand(_ => StartFocus(), parameter => IsFocusIdle && TryReadFocusDuration(out var ignored));
        PauseFocusCommand = new RelayCommand(_ => ApplyFocusTransition(service => service.Pause()), _ => IsFocusRunning);
        ResumeFocusCommand = new RelayCommand(_ => ApplyFocusTransition(service => service.Resume()), _ => IsFocusPaused);
        RequestEndFocusCommand = new RelayCommand(_ => SetFocusEndConfirmation(true), _ => IsFocusActive);
        ConfirmEndFocusCommand = new RelayCommand(_ => EndFocusEarly(), _ => IsFocusActive && ShowFocusEndConfirmation);
        CancelEndFocusCommand = new RelayCommand(_ => SetFocusEndConfirmation(false), _ => ShowFocusEndConfirmation);
        CompleteFocusTodoCommand = new RelayCommand(_ => CompleteLinkedFocusTodo(), _ => CanCompleteFocusTodo);
        CloseFocusResultCommand = new RelayCommand(_ => CloseFocusResult(), _ => ShowFocusResult);
    }

    private void AttachFocus(FocusSessionService focusService)
    {
        if (_focusService is not null) _focusService.Changed -= HandleFocusChanged;
        _focusService = focusService;
        _focusService.Changed += HandleFocusChanged;
        FocusDurationMinutes = focusService.LastDurationMinutes.ToString(CultureInfo.InvariantCulture);
        _showFocusResult = focusService.ConsumeRecoveredCompletion();
        ApplyFocusSnapshot(focusService.Snapshot(), notify: false);
        if (_showFocusResult)
            FocusMessage = "应用关闭期间计时已到，本次专注已完成并写入本机。";
        _focusTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _focusTimer.Tick -= HandleFocusTimer;
        _focusTimer.Tick += HandleFocusTimer;
        if (IsFocusRunning) _focusTimer.Start();
    }

    private void HandleFocusTimer(object? sender, EventArgs e)
    {
        if (_focusService is null) return;
        try { ApplyFocusSnapshot(_focusService.Snapshot()); }
        catch (Exception ex) when (IsFocusPersistenceError(ex))
        {
            FocusMessage = FocusErrorMessage(ex);
        }
    }

    private void HandleFocusChanged(FocusSessionSnapshot snapshot) => ApplyFocusSnapshot(snapshot);

    private void ApplyFocusSnapshot(FocusSessionSnapshot snapshot, bool notify = true)
    {
        var wasActive = _focusSnapshot.IsActive;
        _focusSnapshot = snapshot;
        if (!snapshot.IsActive && _showFocusEndConfirmation)
        {
            _showFocusEndConfirmation = false;
            OnPropertyChanged(nameof(ShowFocusEndConfirmation));
        }
        RefreshFocusAssociation();
        if (snapshot.IsRunning) _focusTimer?.Start(); else _focusTimer?.Stop();
        if (wasActive && snapshot.IsCompleted)
        {
            _showFocusResult = true;
            FocusMessage = "完成得很稳，专注记录已写入本机。";
            FocusCompleted?.Invoke(snapshot);
        }
        RaiseFocusStateChanged();
        if (notify) FocusStateChanged?.Invoke(snapshot);
    }

    private void RefreshFocusAssociation()
    {
        var sessionTodoId = _focusSnapshot.Session?.TodoId;
        if (_focusSnapshot.IsActive || _showFocusResult) _focusTodoId = sessionTodoId;
        if (_focusTodoId is { } id)
        {
            var todo = _store?.Load().FirstOrDefault(item => item.Id == id);
            _focusTodoTitle = todo?.Title ?? "原待办已删除";
        }
        else _focusTodoTitle = string.Empty;
        OnPropertyChanged(nameof(FocusAssociationText));
        OnPropertyChanged(nameof(FocusSelectedTodoHint));
        OnPropertyChanged(nameof(FocusResultText));
        OnPropertyChanged(nameof(CanCompleteFocusTodo));
        RaiseFocusCommands();
    }

    private void UseSelectedTodoForFocus()
    {
        if (SelectedTodo is not { IsCompleted: false, IsReminder: false } selected) return;
        _focusTodoId = selected.Id;
        _focusTodoTitle = selected.Title;
        FocusMessage = "已关联待办；专注结束后可直接标记完成。";
        RefreshFocusAssociation();
    }

    private void ClearFocusAssociation()
    {
        _focusTodoId = null;
        _focusTodoTitle = string.Empty;
        FocusMessage = "本次专注不会关联待办。";
        RefreshFocusAssociation();
    }

    private void StartFocus()
    {
        if (_focusService is null || !TryReadFocusDuration(out var minutes)) return;
        try
        {
            _showFocusResult = false;
            SetFocusEndConfirmation(false);
            ApplyFocusSnapshot(_focusService.Start(_focusTodoId, minutes));
            FocusMessage = "已进入低打扰状态；计时会在重启后继续校准。";
        }
        catch (Exception ex) when (IsFocusPersistenceError(ex))
        {
            FocusMessage = FocusErrorMessage(ex);
        }
    }

    private void ApplyFocusTransition(Func<FocusSessionService, FocusSessionSnapshot> transition)
    {
        if (_focusService is null) return;
        try { ApplyFocusSnapshot(transition(_focusService)); }
        catch (Exception ex) when (IsFocusPersistenceError(ex))
        { FocusMessage = FocusErrorMessage(ex); }
    }

    private void EndFocusEarly()
    {
        if (_focusService is null) return;
        SetFocusEndConfirmation(false);
        try
        {
            var snapshot = _focusService.EndEarly();
            ApplyFocusSnapshot(snapshot);
            _showFocusResult = true;
            FocusMessage = snapshot.IsCompleted
                ? "计时已到，本次专注按完成记录。"
                : "本次专注已提前结束，已用时仍保留在复盘统计中。";
            RaiseFocusStateChanged();
        }
        catch (Exception ex) when (IsFocusPersistenceError(ex))
        { FocusMessage = FocusErrorMessage(ex); }
    }

    private void CompleteLinkedFocusTodo()
    {
        if (_focusSnapshot.Session?.TodoId is not { } id) return;
        var item = _store?.Load().FirstOrDefault(todo => todo.Id == id);
        if (item is not { Status: TodoStatus.Pending }) return;
        Complete(item);
        FocusMessage = "关联待办已标记完成。";
        RaiseFocusStateChanged();
    }

    private void CloseFocusResult()
    {
        _showFocusResult = false;
        _focusTodoId = null;
        _focusTodoTitle = string.Empty;
        FocusMessage = "选择时长即可开始下一次专注。";
        RaiseFocusStateChanged();
    }

    private void SetFocusEndConfirmation(bool visible)
    {
        _showFocusEndConfirmation = visible;
        OnPropertyChanged(nameof(ShowFocusEndConfirmation));
        RaiseFocusCommands();
    }

    private bool TryReadFocusDuration(out int minutes) => int.TryParse(
        FocusDurationMinutes,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out minutes) && minutes is >= 5 and <= 180;

    private static bool IsFocusPersistenceError(Exception ex) =>
        ex is FocusSessionValidationException or FocusSessionConcurrencyException or InvalidDataException or IOException or UnauthorizedAccessException;

    private static string FocusErrorMessage(Exception ex) => ex is IOException or UnauthorizedAccessException
        ? "专注记录未能写入；原数据已保留，请检查数据目录权限和剩余空间后重试。"
        : ex.Message;

    private static string FormatClock(int seconds) => seconds >= 100 * 60
        ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
        : $"{seconds / 60:00}:{seconds % 60:00}";

    private void RaiseFocusStateChanged()
    {
        foreach (var property in new[]
        {
            nameof(CurrentFocus), nameof(IsFocusIdle), nameof(IsFocusRunning), nameof(IsFocusPaused), nameof(IsFocusActive),
            nameof(FocusRemainingText), nameof(FocusElapsedText), nameof(FocusStateText),
            nameof(FocusAssociationText), nameof(FocusResultText), nameof(ShowFocusResult),
            nameof(CanCompleteFocusTodo), nameof(JournalSummaryText),
        }) OnPropertyChanged(property);
        RefreshJournalProjection();
        RaiseFocusCommands();
    }

    private void RaiseFocusCommands()
    {
        foreach (var command in new[]
        {
            UseSelectedTodoForFocusCommand, ClearFocusTodoCommand, StartFocusCommand, PauseFocusCommand,
            ResumeFocusCommand, RequestEndFocusCommand, ConfirmEndFocusCommand, CancelEndFocusCommand,
            CompleteFocusTodoCommand, CloseFocusResultCommand,
        }.OfType<RelayCommand>()) command.RaiseCanExecuteChanged();
    }

    private void CancelFocusWork()
    {
        _focusTimer?.Stop();
        if (_focusService is null) return;
        try { _focusService.Checkpoint(); }
        catch { /* Existing recoverable data remains available after shutdown. */ }
    }
}
