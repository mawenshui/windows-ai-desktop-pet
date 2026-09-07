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
    private bool _readFailed;

    public TodoStore(string? overrideRoot = null, Func<DateTimeOffset>? now = null)
    {
        var root = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsAiDesktopPet");
        TodoPath = Path.Combine(root, "todos.json");
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string TodoPath { get; }
    public event Action? Changed;

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
        AdditionalReminderTimes = Array.Empty<DateTimeOffset>(),
        Recurrence = new(),
        RecurrenceAnchorAt = null,
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
        AdditionalReminderTimes = Array.Empty<DateTimeOffset>(),
        Recurrence = new(),
        RecurrenceAnchorAt = null,
        Status = item.IsReminder ? TodoStatus.Completed : item.Status,
        CompletedAt = item.IsReminder ? _now() : item.CompletedAt,
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
            Status = item.IsReminder ? TodoStatus.Pending : item.Status,
            CompletedAt = item.IsReminder ? null : item.CompletedAt,
            QueuedOccurrenceAt = null,
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

    public TodoItem AdvanceReminder(Guid id, DateTimeOffset deliveredAt, bool queued = false)
    {
        return Mutate(id, item => AdvanceOccurrence(item, deliveredAt, queued), validateScheduledTime: false);
    }

    public TodoItem? DeliverReminderIfCurrent(Guid id, DateTimeOffset expectedAt, DateTimeOffset attemptedAt, Func<TodoItem,bool> deliver, bool queued)
    {
        lock (_gate)
        {
            var item=ReadDocument().Items.FirstOrDefault(item=>item.Id==id);
            if(item is null || item.Status!=TodoStatus.Pending || item.ReminderState is not (ReminderState.Scheduled or ReminderState.Snoozed) || GetNextReminder(item)!=expectedAt) return null;
            // Queue acceptance and the corresponding state update share the store lock,
            // so concurrent edits cannot accidentally consume a newly scheduled occurrence.
            try
            {
                if(!deliver(Clone(item))) { MarkReminderFailed(id,attemptedAt,"NotificationRejected"); return null; }
            }
            catch { MarkReminderFailed(id,attemptedAt,"NotificationException"); return null; }
            return AdvanceReminder(id,attemptedAt,queued);
        }
    }

    public TodoItem SkipOccurrence(Guid id) => Mutate(id, item =>
    {
        var next = GetNextReminder(item) ?? throw new TodoValidationException("没有可跳过的提醒。");
        return AdvanceOccurrence(item, next > _now() ? next : _now()) with { LastReminderAttemptAt = item.LastReminderAttemptAt };
    }, validateScheduledTime: false);

    public TodoItem ConfirmQueuedSubmission(Guid id, DateTimeOffset scheduledAt, DateTimeOffset submittedAt) => Mutate(id, item =>
        item.ReminderState == ReminderState.Queued && item.QueuedOccurrenceAt == scheduledAt
        ? item with { ReminderState=ReminderState.Delivered, LastReminderAttemptAt=submittedAt, Status=item.IsReminder?TodoStatus.Completed:item.Status, CompletedAt=item.IsReminder?submittedAt:item.CompletedAt }
        : item, false);

    private static TodoItem AdvanceOccurrence(TodoItem item, DateTimeOffset deliveredAt, bool queued = false)
        {
            var current = GetNextReminder(item);
            var additional = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>())
                .Where(value => current is null || value != current.Value)
                .Where(value => value > deliveredAt)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            var recurrenceAt = RecurrenceCalculator.Next(item.Recurrence, item.RecurrenceAnchorAt ?? current ?? deliveredAt, deliveredAt);
            var nextAt = additional.Select(value => (DateTimeOffset?)value).Append(recurrenceAt).Where(value => value is not null).Min();
            if (nextAt is null)
            {
                return item with
                {
                    ReminderAt = null,
                    AdditionalReminderTimes = additional,
                    ReminderState = queued ? ReminderState.Queued : ReminderState.Delivered,
                    QueuedOccurrenceAt = queued ? current : null,
                    LastReminderAttemptAt = deliveredAt,
                    ReminderFailureCode = null,
                    Status = item.IsReminder && !queued ? TodoStatus.Completed : item.Status,
                    CompletedAt = item.IsReminder && !queued ? deliveredAt : item.CompletedAt,
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
        }

    public DateTimeOffset? GetNextReminder() => Load()
        .Where(item => item.Status == TodoStatus.Pending)
        .Where(item => item.ReminderState is ReminderState.Scheduled or ReminderState.Snoozed)
        .Select(GetNextReminder)
        .Where(value => value is not null)
        .Min();

    public static DateTimeOffset? GetNextReminder(TodoItem item)
    {
        var values = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>()).AsEnumerable();
        if (item.ReminderAt is { } primary) values = values.Append(primary);
        return values.OrderBy(value => value).Cast<DateTimeOffset?>().FirstOrDefault();
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
        if(item.AdditionalReminderTimes?.Count>32) throw new TodoValidationException("最多设置 32 个额外提醒时间。");
        if (title.Length == 0) throw new TodoValidationException("标题不能为空。");
        if (title.Length > 200) throw new TodoValidationException("标题不能超过 200 个字符。");
        if (notes.Length > 4000) throw new TodoValidationException("备注不能超过 4000 个字符。");
        if (item.IsReminder && item.Status == TodoStatus.Pending && item.ReminderAt is null && item.ReminderState != ReminderState.Queued)
        {
            if (item.AdditionalReminderTimes is null || item.AdditionalReminderTimes.Count == 0)
                throw new TodoValidationException("提醒项必须设置提醒时间。");
        }
        var times = (item.AdditionalReminderTimes ?? Array.Empty<DateTimeOffset>()).Select(value => (DateTimeOffset?)value)
            .Append(item.ReminderAt).Where(value => value is not null).Select(value => value!.Value).Distinct().OrderBy(value => value).ToArray();
        DateTimeOffset? primaryReminder = times.Length == 0 ? null : times[0];
        var additional = times.Skip(1).ToArray();
        if (validateScheduledTime && additional.Any(value => value <= _now()))
            throw new TodoValidationException("所有提醒时间都必须晚于当前时间。");
        var recurrence = item.Recurrence ?? new RecurrenceRule();
        RecurrenceCalculator.Validate(recurrence);
        if (recurrence.Kind != RecurrenceKind.None && primaryReminder is null && item.Status == TodoStatus.Pending && item.ReminderState is not (ReminderState.Delivered or ReminderState.Queued))
            throw new TodoValidationException("重复规则必须设置首次提醒时间。");
        if (validateScheduledTime && recurrence.EndsAt is { } end && primaryReminder > end)
            throw new TodoValidationException("结束时间不能早于首次提醒。");

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
            RecurrenceAnchorAt = recurrence.Kind == RecurrenceKind.None ? null : item.RecurrenceAnchorAt ?? primaryReminder,
        };
    }

    private TodoDocument ReadDocument()
    {
        _readFailed = false;
        try
        {
            if (!File.Exists(TodoPath)) return new TodoDocument();
            var json = File.ReadAllText(TodoPath);
            DataMaintenanceService.ValidateJson("todos.json", System.Text.Encoding.UTF8.GetBytes(json));
            var document = JsonSerializer.Deserialize<TodoDocument>(json, Options) ?? new TodoDocument();
            foreach(var item in document.Items) _=Normalize(item,validateScheduledTime:false);
            if (document.SchemaVersion < 3)
            {
                RecoverableAtomicFile.WriteAllText(TodoPath + ".pre-v3.bak", json);
                document = document with { SchemaVersion = 3, Items = document.Items.Select(item => item with { RecurrenceAnchorAt = item.Recurrence.Kind == RecurrenceKind.None ? null : GetNextReminder(item) }).ToList() };
                WriteDocument(document);
            }
            return document;
        }
        catch
        {
            _readFailed = true;
            return new TodoDocument();
        }
    }

    private void WriteDocument(TodoDocument document)
    {
        if (_readFailed) throw new InvalidDataException("现有待办无法读取，请先恢复备份，原文件保留。");
        var json = JsonSerializer.Serialize(document, Options);
        RecoverableAtomicFile.WriteAllText(TodoPath, json);
        try { Changed?.Invoke(); } catch { /* A listener cannot invalidate a completed write. */ }
    }

    private static TodoItem Clone(TodoItem item) => item with { AdditionalReminderTimes = item.AdditionalReminderTimes.ToArray(), Recurrence = item.Recurrence with { DaysOfWeek = item.Recurrence.DaysOfWeek.ToArray() } };
}
