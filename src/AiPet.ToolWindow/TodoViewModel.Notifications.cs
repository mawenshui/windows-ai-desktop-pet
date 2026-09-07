using System.Collections.ObjectModel;
using System.Windows.Input;
using AiPet.Todos;

namespace AiPet.ToolWindow;

public sealed partial class TodoViewModel
{
    private NotificationCenter? _notifications;
    public ObservableCollection<NotificationEntry> NotificationHistory { get; } = new();
    public IReadOnlyList<int> SnoozeOptions { get; } = new[] {5,10,30,60};
    public int SnoozeMinutes { get; set; } = 10;
    public bool QuietEnabled { get; set; }
    public string QuietStart { get; set; } = "22:00";
    public string QuietEnd { get; set; } = "08:00";
    public string NotificationSummary => $"提醒中心 · {NotificationHistory.Count(entry=>entry.CanHandle)} 条待处理";
    public void AttachNotificationCenter(NotificationCenter center)
    {
        _notifications=center;
        try { var quiet=center.Quiet; QuietEnabled=quiet.Enabled; QuietStart=quiet.Start; QuietEnd=quiet.End; RefreshNotifications(); }
        catch { Status="提醒记录无法读取，请保留本地数据并检查备份。"; }
        OnPropertyChanged(nameof(QuietEnabled)); OnPropertyChanged(nameof(QuietStart)); OnPropertyChanged(nameof(QuietEnd));
    }
    public void RefreshNotifications()
    {
        if (_notifications is null) return;
        var entries=_notifications.Entries.OrderBy(entry=>entry.State is NotificationDeliveryState.Queued or NotificationDeliveryState.Submitted?0:1).ThenByDescending(entry=>entry.QueuedAt).ToArray();
        if (!NotificationHistory.SequenceEqual(entries)) { NotificationHistory.Clear(); foreach (var entry in entries) NotificationHistory.Add(entry); OnPropertyChanged(nameof(NotificationSummary)); }
    }
    public ICommand SaveQuietHoursCommand => new RelayCommand(_=>
    {
        try { _notifications?.SetQuietHours(new(QuietEnabled,QuietStart,QuietEnd)); Status="静默时段已保存；期间提醒排队，结束后合并提交。"; }
        catch (TodoValidationException ex) { Status=ex.Message; }
        catch { Status="静默设置未保存，请重试。"; }
    });
    public ICommand ClearNotificationHistoryCommand => new RelayCommand(_=>
    {
        try { _notifications?.ClearHistory(); RefreshNotifications(); Status="已清除处理和取消历史，未处理提醒与待办保留。"; }
        catch { Status="提醒历史未清除，请重试。"; }
    });
    public ICommand TestNotificationCommand => new RelayCommand(_=>
    {
        try { _notifications?.EnqueueChannelTest(); RefreshNotifications(); Status="通道测试已排队；静默时段结束后提交。"; }
        catch { Status="测试未能排队，请检查本地存储。"; }
    });
    public ICommand CompleteNotificationCommand => new RelayCommand(parameter=>ActOnNotification(parameter,"complete"));
    public ICommand SnoozeNotificationCommand => new RelayCommand(parameter=>ActOnNotification(parameter,"snooze"));
    public ICommand OpenNotificationCommand => new RelayCommand(parameter=>ActOnNotification(parameter,"open"));
    private void ActOnNotification(object? parameter,string action)
    {
        if (parameter is not Guid id || _notifications is null || _store is null) return;
        try
        {
            var entry=_notifications.Entries.FirstOrDefault(entry=>entry.Id==id); if (entry is null) return;
            var item=_store.Load().FirstOrDefault(item=>item.Id==entry.TodoId);
            if (item is null) { _notifications.Handle(id); Status="对应事项已删除，记录已处理。"; RefreshNotifications(); return; }
            if (action=="open") { SelectedFilterId=item.Status==TodoStatus.Completed?"completed":"pending"; OpenEditor(item); return; }
            if (action=="complete") _store.Complete(item.Id);
            else
            {
                if (!SnoozeOptions.Contains(SnoozeMinutes)) throw new TodoValidationException("请选择有效的稍后时长。");
                _store.Snooze(item.Id,_now().AddMinutes(SnoozeMinutes));
            }
            _notifications.CancelForTodo(item.Id); _notifications.Handle(id); RefreshNotifications(); Reload();
            Status=action=="complete"?"事项已完成。":$"已安排 {SnoozeMinutes} 分钟后提醒。";
        }
        catch (TodoValidationException ex) { Status=ex.Message; }
        catch { Status="提醒操作未完成，请重试。"; }
    }
}
