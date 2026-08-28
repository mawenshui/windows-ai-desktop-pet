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

    [JsonPropertyName("status")]
    public TodoStatus Status { get; init; }

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
}

public sealed record TodoDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("items")]
    public List<TodoItem> Items { get; init; } = new();
}
