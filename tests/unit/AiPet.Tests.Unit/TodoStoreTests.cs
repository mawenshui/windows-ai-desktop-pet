using AiPet.Todos;
using System.IO;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class TodoStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aipet-todos-{Guid.NewGuid():N}");

    [Fact]
    public void Todo_round_trip_and_distinct_state_operations_are_persisted()
    {
        var now = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(_root, () => now);
        var created = store.Create(new TodoItem
        {
            Title = "提交周报",
            Notes = "附上本周数据",
            DueAt = now.AddHours(8),
            ReminderAt = now.AddHours(6),
        });

        var reloaded = Assert.Single(new TodoStore(_root, () => now).Load());
        Assert.Equal(created.Id, reloaded.Id);
        Assert.Equal("提交周报", reloaded.Title);
        Assert.Equal(ReminderState.Scheduled, reloaded.ReminderState);

        var completed = store.Complete(created.Id);
        Assert.Equal(TodoStatus.Completed, completed.Status);
        Assert.Null(completed.ReminderAt);
        Assert.Equal(ReminderState.Cancelled, completed.ReminderState);

        var restored = store.Restore(created.Id);
        Assert.Equal(TodoStatus.Pending, restored.Status);
        Assert.Null(restored.ReminderAt);

        now = now.AddMinutes(1);
        var updated = store.Update(restored with
        {
            Title = "提交完整周报",
            ReminderAt = now.AddHours(2),
            ReminderState = ReminderState.Scheduled,
        });
        Assert.Equal("提交完整周报", updated.Title);
        Assert.NotNull(updated.ReminderAt);

        var reminderCancelled = store.CancelReminder(created.Id);
        Assert.Null(reminderCancelled.ReminderAt);
        Assert.Equal(ReminderState.Cancelled, reminderCancelled.ReminderState);

        Assert.NotNull(store.Delete(created.Id));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Corrupted_todo_file_is_left_untouched_and_loaded_as_empty()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "todos.json");
        File.WriteAllText(path, "{ not-json");

        var store = new TodoStore(_root);

        Assert.Empty(store.Load());
        Assert.Equal("{ not-json", File.ReadAllText(path));
    }

    [Fact]
    public void Scheduled_reminder_must_be_in_the_future()
    {
        var now = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(_root, () => now);

        var error = Assert.Throws<TodoValidationException>(() => store.Create(new TodoItem
        {
            Title = "过期提醒",
            ReminderAt = now.AddMinutes(-1),
        }));

        Assert.Contains("晚于当前时间", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Reminder_item_completion_preserves_delivery_state_and_channel_preferences()
    {
        var now = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(_root, () => now);
        var created = store.Create(new TodoItem
        {
            Title = "提醒项",
            IsReminder = true,
            ReminderAt = now.AddHours(1),
            ReminderState = ReminderState.Scheduled,
            ReminderRoamEnabled = true,
            ReminderBubbleEnabled = false,
        });

        var completed = store.CompleteReminder(created.Id, now.AddHours(1));

        Assert.Equal(TodoStatus.Completed, completed.Status);
        Assert.Equal(ReminderState.Delivered, completed.ReminderState);
        Assert.Equal(now.AddHours(1), completed.ReminderAt);
        Assert.True(completed.IsReminder);
        Assert.True(completed.ReminderRoamEnabled);
        Assert.False(completed.ReminderBubbleEnabled);
        Assert.Equal(now.AddHours(1), completed.CompletedAt);
        Assert.Equal(now.AddHours(1), completed.LastReminderAttemptAt);
    }

    [Fact]
    public void Ordinary_todo_cannot_use_reminder_item_auto_completion()
    {
        var now = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(_root, () => now);
        var todo = store.Create(new TodoItem
        {
            Title = "普通待办",
            ReminderAt = now.AddHours(1),
        });

        Assert.Throws<TodoValidationException>(() => store.CompleteReminder(todo.Id, now.AddHours(1)));
        Assert.Equal(TodoStatus.Pending, Assert.Single(store.Load()).Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
