using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.Todos;

public sealed class TodoValidationException : Exception
{
    public TodoValidationException(string message) : base(message) { }
}

public sealed class TodoStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;

    public TodoStore(string? overrideRoot = null, Func<DateTimeOffset>? now = null)
    {
        var root = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsAiDesktopPet");
        TodoPath = Path.Combine(root, "todos.json");
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string TodoPath { get; }

    public IReadOnlyList<TodoItem> Load()
    {
        lock (_gate)
        {
            return ReadDocument().Items.Select(Clone).ToArray();
        }
    }

    public TodoItem Create(TodoItem candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            var document = ReadDocument();
            var now = _now();
            var created = Normalize(candidate with
            {
                Id = candidate.Id == Guid.Empty ? Guid.NewGuid() : candidate.Id,
                CreatedAt = candidate.CreatedAt == default ? now : candidate.CreatedAt,
                UpdatedAt = now,
                CompletedAt = candidate.Status == TodoStatus.Completed
                    ? candidate.CompletedAt ?? now
                    : null,
            }, validateScheduledTime: true);
            if (document.Items.Any(item => item.Id == created.Id))
                throw new TodoValidationException("待办标识已存在。");
            document.Items.Add(created);
            WriteDocument(document);
            return Clone(created);
        }
    }

    public TodoItem Update(TodoItem candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Items.FindIndex(item => item.Id == candidate.Id);
            if (index < 0) throw new TodoValidationException("待办不存在或已被删除。");
            var existing = document.Items[index];
            var updated = Normalize(candidate with
            {
                CreatedAt = existing.CreatedAt,
                UpdatedAt = _now(),
                CompletedAt = candidate.Status == TodoStatus.Completed
                    ? candidate.CompletedAt ?? _now()
                    : null,
            }, validateScheduledTime: true);
            document.Items[index] = updated;
            WriteDocument(document);
            return Clone(updated);
        }
    }

    public TodoItem UpsertSnapshot(TodoItem snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var document = ReadDocument();
            var restored = Normalize(snapshot with { UpdatedAt = _now() }, validateScheduledTime: false);
            var index = document.Items.FindIndex(item => item.Id == restored.Id);
            if (index < 0) document.Items.Add(restored);
            else document.Items[index] = restored;
            WriteDocument(document);
            return Clone(restored);
        }
    }

    public TodoItem? Delete(Guid id)
    {
        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Items.FindIndex(item => item.Id == id);
            if (index < 0) return null;
            var deleted = document.Items[index];
            document.Items.RemoveAt(index);
            WriteDocument(document);
            return Clone(deleted);
        }
    }

    public TodoItem Complete(Guid id) => Mutate(id, item => item with
    {
        Status = TodoStatus.Completed,
        CompletedAt = _now(),
        ReminderAt = null,
        ReminderState = item.ReminderAt is null ? item.ReminderState : ReminderState.Cancelled,
        ReminderFailureCode = null,
    });

    public TodoItem Restore(Guid id) => Mutate(id, item => item with
    {
        Status = TodoStatus.Pending,
        CompletedAt = null,
    });

    public TodoItem CancelReminder(Guid id) => Mutate(id, item => item with
    {
        ReminderAt = null,
        ReminderState = ReminderState.Cancelled,
        ReminderFailureCode = null,
    });

    public TodoItem Snooze(Guid id, DateTimeOffset nextReminderAt)
    {
        if (nextReminderAt <= _now())
            throw new TodoValidationException("稍后提醒时间必须晚于当前时间。");
        return Mutate(id, item => item with
        {
            ReminderAt = nextReminderAt,
            ReminderState = ReminderState.Snoozed,
            ReminderFailureCode = null,
        });
    }

    public TodoItem MarkReminderDelivered(Guid id, DateTimeOffset attemptedAt) =>
        Mutate(id, item => item with
        {
            ReminderState = ReminderState.Delivered,
            LastReminderAttemptAt = attemptedAt,
            ReminderFailureCode = null,
        }, validateScheduledTime: false);

    public TodoItem MarkReminderFailed(Guid id, DateTimeOffset attemptedAt, string failureCode) =>
        Mutate(id, item => item with
        {
            ReminderState = ReminderState.Failed,
            LastReminderAttemptAt = attemptedAt,
            ReminderFailureCode = string.IsNullOrWhiteSpace(failureCode)
                ? "NotificationFailed"
                : failureCode.Trim(),
        }, validateScheduledTime: false);

    private TodoItem Mutate(
        Guid id,
        Func<TodoItem, TodoItem> change,
        bool validateScheduledTime = true)
    {
        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Items.FindIndex(item => item.Id == id);
            if (index < 0) throw new TodoValidationException("待办不存在或已被删除。");
            var updated = Normalize(change(document.Items[index]) with
            {
                UpdatedAt = _now(),
            }, validateScheduledTime);
            document.Items[index] = updated;
            WriteDocument(document);
            return Clone(updated);
        }
    }

    private TodoItem Normalize(TodoItem item, bool validateScheduledTime)
    {
        var title = (item.Title ?? string.Empty).Trim();
        var notes = (item.Notes ?? string.Empty).Trim();
        if (title.Length == 0) throw new TodoValidationException("标题不能为空。");
        if (title.Length > 200) throw new TodoValidationException("标题不能超过 200 个字符。");
        if (notes.Length > 4000) throw new TodoValidationException("备注不能超过 4000 个字符。");

        var reminderState = item.ReminderState;
        if (item.ReminderAt is null && reminderState is ReminderState.Scheduled or ReminderState.Snoozed)
            reminderState = ReminderState.None;
        if (item.ReminderAt is not null && reminderState == ReminderState.None)
            reminderState = ReminderState.Scheduled;
        if (validateScheduledTime
            && item.Status == TodoStatus.Pending
            && item.ReminderAt is { } reminderAt
            && reminderState is ReminderState.Scheduled or ReminderState.Snoozed
            && reminderAt <= _now())
            throw new TodoValidationException("提醒时间必须晚于当前时间。");

        return item with
        {
            Title = title,
            Notes = notes,
            ReminderState = reminderState,
        };
    }

    private TodoDocument ReadDocument()
    {
        try
        {
            if (!File.Exists(TodoPath)) return new TodoDocument();
            var json = File.ReadAllText(TodoPath);
            return JsonSerializer.Deserialize<TodoDocument>(json, Options) ?? new TodoDocument();
        }
        catch
        {
            return new TodoDocument();
        }
    }

    private void WriteDocument(TodoDocument document)
    {
        var json = JsonSerializer.Serialize(document, Options);
        RecoverableAtomicFile.WriteAllText(TodoPath, json);
    }

    private static TodoItem Clone(TodoItem item) => item with { };
}
