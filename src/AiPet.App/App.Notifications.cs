using AiPet.Todos;
using System.Windows.Threading;

namespace AiPet.App;

public partial class App
{
    private NotificationCenter? _notifications;
    private DispatcherTimer? _notificationTimer;
    private bool _notificationStartupReconciled;
    private void ProcessNotifications()
    {
        if (_shutdownRequested || _notifications is null || _homeVm is null || _tray is null) return;
        try
        {
            var entries = _notifications.Entries;
            if (_notificationStartupReconciled && !entries.Any(entry => entry.State == NotificationDeliveryState.Queued)) { _homeVm.Todo.RefreshNotifications(); return; }
            var items=_todoStore!.Load().ToDictionary(item=>item.Id);
            foreach (var entry in _notifications.Entries.Where(entry=>entry.State==NotificationDeliveryState.Queued && entry.TodoId!=Guid.Empty))
                if (!items.TryGetValue(entry.TodoId,out var item) || item.Status==TodoStatus.Completed || item.ReminderState==ReminderState.Cancelled)
                    _notifications.CancelForTodo(entry.TodoId);
            _notifications.SubmitNext(batch=>
            {
                var title=batch.Count>1?$"有 {batch.Count} 条提醒待处理":batch[0].IsRecovery?"错过的提醒":"到期提醒";
                var message=string.Join("；",batch.Take(3).Select(entry=>entry.Title)) + (batch.Count>3?$"；另有 {batch.Count-3} 条，请打开提醒中心":"");
                if (message.Length>240) message=message[..240]+"…";
                _tray.ShowBalloon(title,message);
                var petEntry=batch.FirstOrDefault(entry=>entry.IsReminder && (entry.Bubble||entry.Roam));
                if (petEntry is not null && _pet is not null)
                {
                    // Respect an explicitly hidden pet; the inbox and tray retain the item.
                    if (_pet.IsVisible) _pet.ShowReminderNotification(batch.Count>1?title:petEntry.Title,petEntry.Bubble,petEntry.Roam);
                }
                _tray.ShowNotificationInbox(batch.Count,ShowTodoPage);
                return true;
            });
            foreach (var entry in _notifications.Entries.Where(entry=>entry.State==NotificationDeliveryState.Submitted && entry.TodoId!=Guid.Empty))
                if (items.TryGetValue(entry.TodoId,out var item) && item.ReminderState==ReminderState.Queued && item.QueuedOccurrenceAt==entry.ScheduledAt)
                    _todoStore.ConfirmQueuedSubmission(entry.TodoId,entry.ScheduledAt,entry.SubmittedAt??DateTimeOffset.Now);
            _notificationStartupReconciled = true;
            _homeVm.Todo.RefreshNotifications(); _homeVm.Todo.RefreshItems();
        }
        catch { DebugLog("[Notifications] queue_processing_failed"); }
    }
}
