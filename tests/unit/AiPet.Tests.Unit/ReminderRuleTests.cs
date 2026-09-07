using System.IO;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ReminderRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-rules-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Earliest_extra_time_becomes_primary_and_is_delivered_once()
    {
        var store = new TodoStore(_root, () => _now);
        var item = store.Create(new TodoItem { Title = "example", ReminderAt = _now.AddHours(3), AdditionalReminderTimes = new[] { _now.AddHours(1), _now.AddHours(3) } });
        Assert.Equal(_now.AddHours(1), item.ReminderAt);
        Assert.Equal(new[] { _now.AddHours(3) }, item.AdditionalReminderTimes);
        Assert.Equal(_now.AddHours(3), store.AdvanceReminder(item.Id, _now.AddHours(1)).ReminderAt);
    }

    [Fact]
    public void Weeks_offline_produce_one_recovery_then_future_occurrence()
    {
        var store = new TodoStore(_root, () => _now);
        var item = store.Create(new TodoItem { Title = "example", IsReminder = true, ReminderAt = _now.AddHours(1), Recurrence = new() { Kind = RecurrenceKind.Daily } });
        var advanced = store.AdvanceReminder(item.Id, _now.AddDays(40).AddHours(2));
        Assert.Equal(_now.AddDays(41).AddHours(1), advanced.ReminderAt);
        Assert.Equal(TodoStatus.Pending, advanced.Status);
    }

    [Fact]
    public void Cancelling_entire_rule_removes_every_scheduled_time()
    {
        var store = new TodoStore(_root, () => _now);
        var item = store.Create(new TodoItem { Title = "example", ReminderAt = _now.AddHours(1), AdditionalReminderTimes = new[] { _now.AddHours(3) }, Recurrence = new() { Kind = RecurrenceKind.Daily } });
        var cancelled = store.CancelReminder(item.Id);
        Assert.Null(cancelled.ReminderAt);
        Assert.Empty(cancelled.AdditionalReminderTimes);
        Assert.Equal(RecurrenceKind.None, cancelled.Recurrence.Kind);
        Assert.Null(store.GetNextReminder());
    }

    [Theory]
    [InlineData(2024, 2, 28, 2024, 2, 29)]
    [InlineData(2024, 2, 29, 2024, 3, 1)]
    [InlineData(2026, 12, 31, 2027, 1, 1)]
    public void Calendar_boundaries_keep_wall_clock(int y, int m, int d, int ny, int nm, int nd)
    {
        var anchor = new DateTimeOffset(y, m, d, 23, 59, 0, TimeSpan.FromHours(8));
        Assert.Equal(new DateTimeOffset(ny, nm, nd, 23, 59, 0, TimeSpan.FromHours(8)), RecurrenceCalculator.Next(new() { Kind = RecurrenceKind.Daily, TimeZoneId = "China Standard Time" }, anchor, anchor));
    }

    [Fact]
    public void Dst_gap_moves_to_first_valid_minute_and_overlap_runs_later_once()
    {
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Daily, TimeZoneId = "Eastern Standard Time" };
        var spring = new DateTimeOffset(2026, 3, 7, 2, 30, 0, TimeSpan.FromHours(-5));
        var gap = RecurrenceCalculator.Next(rule, spring, spring);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)), gap);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 2, 30, 0, TimeSpan.FromHours(-4)), RecurrenceCalculator.Next(rule, spring, gap!.Value));
        var autumn = new DateTimeOffset(2026, 10, 31, 1, 30, 0, TimeSpan.FromHours(-4));
        var overlap = RecurrenceCalculator.Next(rule, autumn, autumn);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-5)), overlap);
        Assert.Equal(2, RecurrenceCalculator.Next(rule, autumn, overlap!.Value)!.Value.Day);
    }

    [Fact]
    public void Fixed_zone_ignores_current_system_offset_and_respects_end()
    {
        var anchor = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.FromHours(8));
        var rule = new RecurrenceRule { Kind = RecurrenceKind.Weekdays, TimeZoneId = "China Standard Time", EndsAt = anchor.AddDays(3) };
        var next = RecurrenceCalculator.Next(rule, anchor, anchor.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Equal(anchor.AddDays(3), next);
        Assert.Null(RecurrenceCalculator.Next(rule, anchor, next!.Value));
    }

    [Fact]
    public void Custom_days_validate_and_skip_does_not_claim_delivery()
    {
        Assert.Throws<TodoValidationException>(() => RecurrenceCalculator.Validate(new() { Kind = RecurrenceKind.CustomDays }));
        var store = new TodoStore(_root, () => _now);
        var item = store.Create(new TodoItem { Title = "example", IsReminder = true, ReminderAt = _now.AddHours(1), Recurrence = new() { Kind = RecurrenceKind.Daily } });
        var skipped = store.SkipOccurrence(item.Id);
        Assert.Equal(_now.AddDays(1).AddHours(1), skipped.ReminderAt);
        Assert.Null(skipped.LastReminderAttemptAt);
        Assert.Equal(TodoStatus.Pending, skipped.Status);
        Assert.Equal(TodoStatus.Completed, store.CancelReminder(item.Id).Status);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
