using System.Text.Json.Serialization;

namespace AiPet.Todos;

public sealed record DailyJournalSnapshotItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; init; }

    [JsonPropertyName("wasPlanned")]
    public bool WasPlanned { get; init; }

    [JsonPropertyName("relevantAt")]
    public DateTimeOffset? RelevantAt { get; init; }
}

public sealed record DailyJournalEntry
{
    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("finalizedAt")]
    public DateTimeOffset? FinalizedAt { get; init; }

    [JsonPropertyName("snapshot")]
    public IReadOnlyList<DailyJournalSnapshotItem> Snapshot { get; init; } =
        Array.Empty<DailyJournalSnapshotItem>();
}

public sealed record DailyJournalDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("activeDate")]
    public DateOnly? ActiveDate { get; init; }

    [JsonPropertyName("entries")]
    public List<DailyJournalEntry> Entries { get; init; } = new();
}

public sealed class DailyJournalValidationException : Exception
{
    public DailyJournalValidationException(string message) : base(message) { }
}

public sealed class DailyJournalConcurrencyException : Exception
{
    public DailyJournalConcurrencyException(string message) : base(message) { }
}
