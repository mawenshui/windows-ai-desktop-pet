using System.IO;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class NotificationCenterTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"aipet-inbox-"+Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Concurrent_due_items_are_durable_deduplicated_and_submitted_as_one_summary()
    {
        var now=new DateTimeOffset(2026,9,6,12,0,0,TimeSpan.Zero); var clock=now.AddHours(-1);
        var todos=new TodoStore(_root,()=>clock); var center=new NotificationCenter(_root,()=>clock,()=>TimeZoneInfo.Utc);
        var items=Enumerable.Range(0,20).Select(number=>todos.Create(new TodoItem {Title=$"fixture {number}",IsReminder=true,ReminderAt=now.AddMinutes(-1)})).ToArray();
        clock=now; using var scheduler=new ReminderScheduler(todos,()=>clock,queuesNotifications:true);
        Assert.Equal(20,await scheduler.CheckNowAsync(center.Enqueue));
        Assert.All(todos.Load(),item=> { Assert.Equal(TodoStatus.Pending,item.Status); Assert.Equal(ReminderState.Queued,item.ReminderState); });
        Assert.True(center.Enqueue(new(items[0],true))); Assert.Equal(20,center.Entries.Count);
        var restarted=new NotificationCenter(_root,()=>clock,()=>TimeZoneInfo.Utc); var calls=0;
        Assert.Equal(20,restarted.SubmitNext(batch=> { calls++; Assert.Equal(20,batch.Count); return true; }));
        Assert.Equal(1,calls); Assert.Equal(0,restarted.SubmitNext(_=>throw new Exception()));
        foreach (var entry in restarted.Entries) todos.ConfirmQueuedSubmission(entry.TodoId,entry.ScheduledAt,clock);
        Assert.All(todos.Load(),item=>Assert.Equal(TodoStatus.Completed,item.Status));
        Assert.All(restarted.Entries,entry=>Assert.Contains("展示未确认",entry.StateText));
        restarted.ClearHistory(); Assert.Equal(20,restarted.Entries.Count);
        foreach (var entry in restarted.Entries) restarted.Handle(entry.Id);
        restarted.ClearHistory(); Assert.Empty(restarted.Entries); Assert.Equal(20,todos.Load().Count);
    }
    [Fact]
    public void Cross_midnight_quiet_hours_defer_and_later_merge_without_losing_items()
    {
        var clock=new DateTimeOffset(2026,9,6,23,0,0,TimeSpan.Zero); var center=new NotificationCenter(_root,()=>clock,()=>TimeZoneInfo.Utc);
        center.SetQuietHours(new(true,"22:00","08:00"));
        center.Enqueue(new(new TodoItem {Id=Guid.NewGuid(),Title="fixture",ReminderAt=clock},false));
        Assert.Equal(0,center.SubmitNext(_=>throw new Exception())); center.ClearHistory(); Assert.Single(center.Entries);
        clock=clock.AddHours(9);
        Assert.Equal(1,center.SubmitNext(_=>true));
        Assert.Throws<TodoValidationException>(()=>center.SetQuietHours(new(true,"08:00","08:00")));
    }
    [Fact]
    public void Clear_history_preserves_queued_channel_tests_until_submission()
    {
        var center = new NotificationCenter(_root);
        center.EnqueueChannelTest();
        center.ClearHistory();
        Assert.Equal(NotificationDeliveryState.Queued, Assert.Single(center.Entries).State);
        Assert.Equal(1, center.SubmitNext(_ => true));
        center.ClearHistory();
        Assert.Empty(center.Entries);
    }
    [Fact]
    public void Rejected_submission_stays_queued_and_cancelled_todo_is_not_submitted()
    {
        var center=new NotificationCenter(_root); var id=Guid.NewGuid();
        center.Enqueue(new(new TodoItem {Id=id,Title="fixture",ReminderAt=DateTimeOffset.Now},false));
        Assert.Equal(0,center.SubmitNext(_=>false)); Assert.Equal(NotificationDeliveryState.Queued,Assert.Single(center.Entries).State);
        center.CancelForTodo(id); Assert.Equal(0,center.SubmitNext(_=>throw new Exception()));
    }
    [Fact]
    public async Task Submitted_independent_reminder_can_be_snoozed_atomically()
    {
        var clock = DateTimeOffset.Now;
        var store = new TodoStore(_root,()=>clock);
        var item = store.Create(new TodoItem { Title="fixture", IsReminder=true, ReminderAt=clock.AddMinutes(1) });
        clock=clock.AddMinutes(2);
        var center = new NotificationCenter(_root,()=>clock);
        using var scheduler = new ReminderScheduler(store,()=>clock,queuesNotifications:true);
        await scheduler.CheckNowAsync(center.Enqueue);
        center.SubmitNext(_=>true);
        var entry = Assert.Single(center.Entries);
        store.ConfirmQueuedSubmission(item.Id,entry.ScheduledAt,clock);
        var snoozed = store.Snooze(item.Id,clock.AddMinutes(10));
        Assert.Equal(TodoStatus.Pending,snoozed.Status);
        Assert.Null(snoozed.CompletedAt);
        Assert.Equal(ReminderState.Snoozed,snoozed.ReminderState);
        Assert.Equal(clock.AddMinutes(10),store.GetNextReminder());
    }
    [Fact]
    public async Task Idle_scheduler_does_not_spin_and_wakes_for_new_items()
    {
        var reads=0;
        var store=new TodoStore(_root);
        using var scheduler=new ReminderScheduler(store,()=> { Interlocked.Increment(ref reads); return DateTimeOffset.Now; });
        var delivered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Delivered += _ => delivered.TrySetResult();
        scheduler.Start(_=>true);
        await Task.Delay(150);
        Assert.InRange(Volatile.Read(ref reads),1,5);
        store.Create(new TodoItem {Title="fixture",ReminderAt=DateTimeOffset.Now.AddMilliseconds(200)});
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Dispose(); await scheduler.WaitForIdleAsync();
    }
    [Fact]
    public void Concurrent_reschedule_is_not_consumed_by_an_old_due_snapshot()
    {
        var clock=DateTimeOffset.Now; var store=new TodoStore(_root,()=>clock);
        var item=store.Create(new TodoItem {Title="fixture",ReminderAt=clock.AddMinutes(1)});
        clock=clock.AddMinutes(2);
        store.Snooze(item.Id,clock.AddHours(1));
        Assert.Null(store.DeliverReminderIfCurrent(item.Id,item.ReminderAt!.Value,clock,_=>throw new Exception("must not deliver stale occurrence"),true));
        Assert.Equal(clock.AddHours(1),store.GetNextReminder());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
}
