using AiPet.Todos;
using System.IO;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ReminderSchedulerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aipet-reminders-{Guid.NewGuid():N}");

    [Fact]
    public async Task Due_reminder_is_delivered_once_and_recovery_is_identified()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = now.AddMinutes(-10);
        var store = new TodoStore(_root, () => clock);
        var item = store.Create(new TodoItem
        {
            Title = "提交周报",
            ReminderAt = now.AddMinutes(-2),
        });
        clock = now;
        using var scheduler = new ReminderScheduler(store, () => clock);
        var calls = 0;
        var wasRecovery = false;

        Assert.Equal(1, await scheduler.CheckNowAsync(notification =>
        {
            calls++;
            wasRecovery = notification.IsRecovery;
            return true;
        }));
        Assert.Equal(0, await scheduler.CheckNowAsync(_ =>
        {
            calls++;
            return true;
        }));

        Assert.Equal(1, calls);
        Assert.True(wasRecovery);
        Assert.Equal(ReminderState.Delivered, Assert.Single(store.Load()).ReminderState);
        Assert.Equal(item.Id, Assert.Single(store.Load()).Id);
    }

    [Fact]
    public async Task Rejected_notification_is_failed_not_successful_or_retried()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = now.AddMinutes(-5);
        var store = new TodoStore(_root, () => clock);
        store.Create(new TodoItem { Title = "开会", ReminderAt = now.AddMinutes(-1) });
        clock = now;
        using var scheduler = new ReminderScheduler(store, () => clock);

        Assert.Equal(0, await scheduler.CheckNowAsync(_ => false));
        Assert.Equal(0, await scheduler.CheckNowAsync(_ => true));

        var failed = Assert.Single(store.Load());
        Assert.Equal(ReminderState.Failed, failed.ReminderState);
        Assert.Equal("NotificationRejected", failed.ReminderFailureCode);
    }

    [Fact]
    public async Task Completed_todo_does_not_deliver_a_reminder()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = now.AddMinutes(-10);
        var store = new TodoStore(_root, () => clock);
        var item = store.Create(new TodoItem { Title = "整理资料", ReminderAt = now.AddMinutes(1) });
        store.Complete(item.Id);
        clock = now.AddMinutes(2);
        using var scheduler = new ReminderScheduler(store, () => clock);

        Assert.Equal(0, await scheduler.CheckNowAsync(_ => true));
    }

    [Fact]
    public async Task Reminder_item_is_completed_after_delivery_and_exposes_pet_channels()
    {
        var now = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(8));
        var clock = now.AddMinutes(-5);
        var store = new TodoStore(_root, () => clock);
        var item = store.Create(new TodoItem
        {
            Title = "站起来活动",
            IsReminder = true,
            ReminderAt = now.AddMinutes(-1),
            ReminderRoamEnabled = true,
            ReminderBubbleEnabled = true,
        });
        clock = now;
        using var scheduler = new ReminderScheduler(store, () => clock);
        ReminderNotification? received = null;

        Assert.Equal(1, await scheduler.CheckNowAsync(notification =>
        {
            received = notification;
            return true;
        }));

        Assert.NotNull(received);
        Assert.True(received!.Item.IsReminder);
        Assert.True(received.Item.ReminderRoamEnabled);
        Assert.True(received.Item.ReminderBubbleEnabled);
        var completed = Assert.Single(store.Load());
        Assert.Equal(item.Id, completed.Id);
        Assert.Equal(TodoStatus.Completed, completed.Status);
        Assert.Equal(ReminderState.Delivered, completed.ReminderState);
        Assert.Equal(0, await scheduler.CheckNowAsync(_ => true));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
