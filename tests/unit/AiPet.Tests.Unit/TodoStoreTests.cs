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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
