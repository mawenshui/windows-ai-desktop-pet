using System.Text.Json.Serialization;

namespace AiPet.Todos;

public enum TodoStatus
{
    Pending = 0,
    Completed = 1,
}

public enum ReminderState
{
    None = 0,
    Scheduled = 1,
    Delivered = 2,
    Snoozed = 3,
    Cancelled = 4,
    Failed = 5,
    Queued = 6,
}

public enum RecurrenceKind
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Weekdays = 3,
    CustomDays = 4,
}

public sealed record RecurrenceRule
{
    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; init; } = string.Empty;
    [JsonPropertyName("kind")]
    public RecurrenceKind Kind { get; init; }
    [JsonPropertyName("interval")]
    public int Interval { get; init; } = 1;
    [JsonPropertyName("daysOfWeek")]
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; } = Array.Empty<DayOfWeek>();
    [JsonPropertyName("endsAt")]
    public DateTimeOffset? EndsAt { get; init; }
}

public sealed record TodoItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("dueAt")]
    public DateTimeOffset? DueAt { get; init; }

    [JsonPropertyName("reminderAt")]
    public DateTimeOffset? ReminderAt { get; init; }

    [JsonPropertyName("additionalReminderTimes")]
    public IReadOnlyList<DateTimeOffset> AdditionalReminderTimes { get; init; } = Array.Empty<DateTimeOffset>();

    [JsonPropertyName("recurrence")]
    public RecurrenceRule Recurrence { get; init; } = new();

    [JsonPropertyName("recurrenceAnchorAt")]
    public DateTimeOffset? RecurrenceAnchorAt { get; init; }
    [JsonPropertyName("queuedOccurrenceAt")]
    public DateTimeOffset? QueuedOccurrenceAt { get; init; }

    [JsonPropertyName("status")]
    public TodoStatus Status { get; init; }

    /// <summary>
    /// Distinguishes a reminder-only entry from an ordinary todo. Missing
    /// values in schema v1 data deserialize to false, so existing records
    /// retain their original todo semantics.
    /// </summary>
    [JsonPropertyName("isReminder")]
    public bool IsReminder { get; init; }

    [JsonPropertyName("reminderState")]
    public ReminderState ReminderState { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("lastReminderAttemptAt")]
    public DateTimeOffset? LastReminderAttemptAt { get; init; }

    [JsonPropertyName("reminderFailureCode")]
    public string? ReminderFailureCode { get; init; }

    /// <summary>When true, a reminder-only entry may move the pet at delivery.</summary>
    [JsonPropertyName("reminderRoamEnabled")]
    public bool ReminderRoamEnabled { get; init; }

    /// <summary>When true, a reminder-only entry shows a bubble beside the pet.</summary>
    [JsonPropertyName("reminderBubbleEnabled")]
    public bool ReminderBubbleEnabled { get; init; } = true;
}

public sealed record TodoDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 3;

    [JsonPropertyName("items")]
    public List<TodoItem> Items { get; init; } = new();
}
