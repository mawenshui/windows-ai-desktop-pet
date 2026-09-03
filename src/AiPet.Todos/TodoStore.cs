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

    /// <summary>
    /// Completes a reminder-only entry after its configured delivery channels
    /// have accepted the notification.  Unlike <see cref="Complete"/>, the
    /// delivery state remains <see cref="ReminderState.Delivered"/> so the
    /// record accurately explains why the completed entry is no longer
    /// scheduled.
    /// </summary>
    public TodoItem CompleteReminder(Guid id) => CompleteReminder(id, _now());

    public TodoItem CompleteReminder(Guid id, DateTimeOffset attemptedAt) => Mutate(id, item =>
    {
        if (!item.IsReminder)
            throw new TodoValidationException("只有提醒项可以在投递后自动完成。");
        return item with
        {
            Status = TodoStatus.Completed,
            CompletedAt = attemptedAt,
            ReminderState = ReminderState.Delivered,
            LastReminderAttemptAt = attemptedAt,
            ReminderFailureCode = null,
        };
    }, validateScheduledTime: false);

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

    public TodoItem AdvanceReminder(Guid id, DateTimeOffset deliveredAt)
    {
        return Mutate(id, item =>
        {
            var current = GetNextReminder(item);
            var additional = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>())
                .Where(value => current is null || value != current.Value)
                .Where(value => value > deliveredAt)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            var next = additional.FirstOrDefault();
            DateTimeOffset? nextAt = next == default ? NextRecurrence(item, current ?? deliveredAt) : next;
            if (nextAt is null)
            {
                return item with
                {
                    ReminderAt = null,
                    AdditionalReminderTimes = additional,
                    ReminderState = ReminderState.Delivered,
                    LastReminderAttemptAt = deliveredAt,
                    ReminderFailureCode = null,
                    Status = item.IsReminder ? TodoStatus.Completed : item.Status,
                    CompletedAt = item.IsReminder ? deliveredAt : item.CompletedAt,
                };
            }
            return item with
            {
                ReminderAt = nextAt,
                AdditionalReminderTimes = additional.Where(value => value != nextAt.Value).ToArray(),
                ReminderState = ReminderState.Scheduled,
                LastReminderAttemptAt = deliveredAt,
                ReminderFailureCode = null,
            };
        }, validateScheduledTime: false);
    }

    public DateTimeOffset? GetNextReminder() => Load()
        .Where(item => item.Status == TodoStatus.Pending)
        .Where(item => item.ReminderState is ReminderState.Scheduled or ReminderState.Snoozed)
        .Select(GetNextReminder)
        .Where(value => value is not null)
        .Min();

    private static DateTimeOffset? GetNextReminder(TodoItem item)
    {
        var values = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>()).AsEnumerable();
        if (item.ReminderAt is { } primary) values = values.Append(primary);
        return values.OrderBy(value => value).Cast<DateTimeOffset?>().FirstOrDefault();
    }

    private static DateTimeOffset? NextRecurrence(TodoItem item, DateTimeOffset after)
    {
        var rule = item.Recurrence ?? new RecurrenceRule();
        if (rule.Kind == RecurrenceKind.None) return null;
        var interval = Math.Clamp(rule.Interval, 1, 365);
        DateTimeOffset candidate = rule.Kind switch
        {
            RecurrenceKind.Daily => after.AddDays(interval),
            RecurrenceKind.Weekly => after.AddDays(7 * interval),
            RecurrenceKind.Weekdays => NextMatchingDay(after, new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }),
            RecurrenceKind.CustomDays => NextMatchingDay(after, rule.DaysOfWeek),
            _ => after.AddDays(interval),
        };
        return rule.EndsAt is { } end && candidate > end ? null : candidate;
    }

    private static DateTimeOffset NextMatchingDay(DateTimeOffset after, IEnumerable<DayOfWeek> days)
    {
        var allowed = days.Distinct().ToHashSet();
        if (allowed.Count == 0) allowed.Add(after.DayOfWeek);
        var candidate = after;
        do { candidate = candidate.AddDays(1); } while (!allowed.Contains(candidate.DayOfWeek));
        return candidate;
    }

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
        if (item.IsReminder && item.Status == TodoStatus.Pending && item.ReminderAt is null)
        {
            if (item.AdditionalReminderTimes is null || item.AdditionalReminderTimes.Count == 0)
                throw new TodoValidationException("提醒项必须设置提醒时间。");
        }
        var additional = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>()).Distinct().OrderBy(value => value).ToArray();
        var primaryReminder = item.ReminderAt;
        if (primaryReminder is null && additional.Length > 0)
        {
            primaryReminder = additional[0];
            additional = additional.Skip(1).ToArray();
        }
        if (validateScheduledTime && additional.Any(value => value <= _now()))
            throw new TodoValidationException("所有提醒时间都必须晚于当前时间。");
        var recurrence = item.Recurrence ?? new RecurrenceRule();
        if (recurrence.Interval is < 1 or > 365)
            throw new TodoValidationException("重复间隔必须在 1 到 365 之间。");

        var reminderState = item.ReminderState;
        if (primaryReminder is null && reminderState is ReminderState.Scheduled or ReminderState.Snoozed)
            reminderState = ReminderState.None;
        if (primaryReminder is not null && reminderState == ReminderState.None)
            reminderState = ReminderState.Scheduled;
        if (validateScheduledTime
            && item.Status == TodoStatus.Pending
            && primaryReminder is { } reminderAt
            && reminderState is ReminderState.Scheduled or ReminderState.Snoozed
            && reminderAt <= _now())
            throw new TodoValidationException("提醒时间必须晚于当前时间。");

        return item with
        {
            Title = title,
            Notes = notes,
            ReminderAt = primaryReminder,
            ReminderState = reminderState,
            AdditionalReminderTimes = additional,
            Recurrence = recurrence,
        };
    }

    private TodoDocument ReadDocument()
    {
        try
        {
            if (!File.Exists(TodoPath)) return new TodoDocument();
            var json = File.ReadAllText(TodoPath);
            var document = JsonSerializer.Deserialize<TodoDocument>(json, Options) ?? new TodoDocument();
            if (document.SchemaVersion < 2)
            {
                RecoverableAtomicFile.WriteAllText(TodoPath + ".pre-v2.bak", json);
                document = document with { SchemaVersion = 2 };
                WriteDocument(document);
            }
            return document;
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
