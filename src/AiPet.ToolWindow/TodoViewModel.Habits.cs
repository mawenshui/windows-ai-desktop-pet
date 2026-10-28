using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed record HabitRowViewModel(Guid Id, string Title, bool Enabled, bool CheckedToday, string ProgressText)
{
    public string TodayText => !Enabled ? "已停用" : CheckedToday ? "今日已打卡" : "今日未打卡";
    public string CheckInLabel => CheckedToday ? "撤销今日打卡" : "今日打卡";
    public string EnableLabel => Enabled ? "停用" : "启用";
}

public sealed partial class TodoViewModel
{
    private HabitStore? _habitStore;
    private DispatcherTimer? _habitDayTimer;
    private DateOnly _habitDisplayDate;
    private string _newHabitTitle = string.Empty;
    private string _habitStatus = "习惯由你手动打卡；错过一天不会扣除已有记录。";
    private bool _showHabitClearConfirmation;

    public ObservableCollection<HabitRowViewModel> Habits { get; } = new();
    public bool HasHabits => Habits.Count > 0;
    public bool ShowHabitClearConfirmation => _showHabitClearConfirmation;
    public string HabitDateText => _habitDisplayDate == default ? string.Empty : $"{_habitDisplayDate:yyyy 年 M 月 d 日} · 本机记录";
    public string HabitStatus
    {
        get => _habitStatus;
        private set { if (_habitStatus == value) return; _habitStatus = value; OnPropertyChanged(); }
    }
    public string NewHabitTitle
    {
        get => _newHabitTitle;
        set
        {
            value ??= string.Empty;
            if (_newHabitTitle == value) return;
            _newHabitTitle = value;
            OnPropertyChanged();
            (AddHabitCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand AddHabitCommand { get; private set; } = null!;
    public ICommand ToggleHabitCheckInCommand { get; private set; } = null!;
    public ICommand ToggleHabitEnabledCommand { get; private set; } = null!;
    public ICommand RequestClearHabitsCommand { get; private set; } = null!;
    public ICommand ConfirmClearHabitsCommand { get; private set; } = null!;
    public ICommand CancelClearHabitsCommand { get; private set; } = null!;

    private void InitializeHabitCommands()
    {
        AddHabitCommand = new RelayCommand(_ => AddHabit(), _ => _habitStore is not null && NewHabitTitle.Trim().Length is >= 1 and <= 80 && Habits.Count < HabitStore.MaximumHabits);
        ToggleHabitCheckInCommand = new RelayCommand(parameter => ToggleHabitCheckIn(parameter));
        ToggleHabitEnabledCommand = new RelayCommand(parameter => ToggleHabitEnabled(parameter));
        RequestClearHabitsCommand = new RelayCommand(_ => SetHabitClearConfirmation(true), _ => HasHabits);
        ConfirmClearHabitsCommand = new RelayCommand(_ => ClearHabits(), _ => ShowHabitClearConfirmation);
        CancelClearHabitsCommand = new RelayCommand(_ => SetHabitClearConfirmation(false), _ => ShowHabitClearConfirmation);
    }

    private void AttachHabits(HabitStore habitStore)
    {
        _habitStore = habitStore;
        RefreshHabitDay(force: true);
        _habitDayTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _habitDayTimer.Tick -= HandleHabitDayTimer;
        _habitDayTimer.Tick += HandleHabitDayTimer;
        _habitDayTimer.Start();
    }

    private void HandleHabitDayTimer(object? sender, EventArgs e) => RefreshHabitDay();

    public void RefreshHabitDay(bool force = false)
    {
        var today = DateOnly.FromDateTime(_now().LocalDateTime);
        if (!force && today == _habitDisplayDate) return;
        _habitDisplayDate = today;
        try
        {
            var entries = _habitStore?.Load().Entries ?? new List<HabitEntry>();
            Habits.Clear();
            if (_habitStore?.HasReadError == true)
            {
                HabitStatus = "习惯记录无法读取；原文件已保留，请从备份恢复。";
                OnPropertyChanged(nameof(HasHabits));
                return;
            }
            foreach (var entry in entries.OrderBy(item => item.CreatedAt))
            {
                var recent = entry.CheckIns.Count(day => day >= today.AddDays(-6) && day <= today);
                Habits.Add(new HabitRowViewModel(entry.Id, entry.Title, entry.Enabled,
                    entry.CheckIns.Contains(today), $"累计 {entry.CheckIns.Count} 天 · 近 7 天 {recent} 天"));
            }
            OnPropertyChanged(nameof(HasHabits));
            OnPropertyChanged(nameof(HabitDateText));
            (AddHabitCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RequestClearHabitsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch
        {
            HabitStatus = "习惯记录无法读取；原文件已保留，请从备份恢复。";
        }
    }

    private void AddHabit()
    {
        if (_habitStore is null) return;
        try
        {
            _habitStore.Create(NewHabitTitle, _now());
            NewHabitTitle = string.Empty;
            RefreshHabitDay(force: true);
            HabitStatus = "习惯已保存在本机。";
        }
        catch { HabitStatus = "习惯未能保存；请检查名称或本地数据状态。"; }
    }

    private void ToggleHabitCheckIn(object? parameter)
    {
        if (_habitStore is null || parameter is not Guid id) return;
        // The minute timer may not have refreshed the row yet after midnight.
        RefreshHabitDay();
        var row = Habits.FirstOrDefault(item => item.Id == id);
        if (row is null || !row.Enabled) return;
        try
        {
            var today = DateOnly.FromDateTime(_now().LocalDateTime);
            _habitStore.SetChecked(id, today, !row.CheckedToday);
            RefreshHabitDay(force: true);
            HabitStatus = row.CheckedToday ? "已撤销今天的打卡。" : "今天的打卡已保存在本机。";
        }
        catch { HabitStatus = "打卡未能保存；原记录已保留。"; }
    }

    private void ToggleHabitEnabled(object? parameter)
    {
        if (_habitStore is null || parameter is not Guid id) return;
        var row = Habits.FirstOrDefault(item => item.Id == id);
        if (row is null) return;
        try
        {
            _habitStore.SetEnabled(id, !row.Enabled);
            RefreshHabitDay(force: true);
            HabitStatus = row.Enabled ? "习惯已停用；历史打卡仍保留。" : "习惯已启用。";
        }
        catch { HabitStatus = "习惯状态未能保存；原记录已保留。"; }
    }

    private void SetHabitClearConfirmation(bool visible)
    {
        _showHabitClearConfirmation = visible;
        OnPropertyChanged(nameof(ShowHabitClearConfirmation));
        (ConfirmClearHabitsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelClearHabitsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public bool HandleHabitEscape()
    {
        if (!ShowHabitClearConfirmation) return false;
        SetHabitClearConfirmation(false);
        return true;
    }

    private void ClearHabits()
    {
        if (_habitStore is null) return;
        try
        {
            _habitStore.ClearAll();
            SetHabitClearConfirmation(false);
            RefreshHabitDay(force: true);
            HabitStatus = "全部习惯和打卡历史已从本机清空。";
        }
        catch { HabitStatus = "清空失败；原记录已保留。"; }
    }

    private void CancelHabitWork() => _habitDayTimer?.Stop();
}
