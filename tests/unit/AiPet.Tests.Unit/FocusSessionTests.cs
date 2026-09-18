using System.IO;
using AiPet.Storage;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class FocusSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-focus-" + Guid.NewGuid().ToString("N"));

    public FocusSessionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Running_pause_resume_and_early_end_preserve_actual_elapsed_time()
    {
        var now = new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.FromHours(8));
        long ticks = 0;
        var service = CreateService(() => now, () => ticks);

        service.Start(null, 25);
        ticks += 75;
        var paused = service.Pause();
        ticks += 600;
        Assert.Equal(75, paused.ElapsedSeconds);
        Assert.Equal(75, service.Snapshot().ElapsedSeconds);

        service.Resume();
        ticks += 45;
        var ended = service.EndEarly();

        Assert.Equal(FocusSessionStatus.EndedEarly, ended.Session!.Status);
        Assert.Equal(120, ended.ElapsedSeconds);
        Assert.False(ended.IsActive);
    }

    [Fact]
    public void Elapsed_plan_completes_exactly_once_and_is_summarized_by_local_end_date()
    {
        // Use an offset that differs from both UTC CI and the usual developer
        // zone to prove daily summaries use the recorded local end date.
        var now = new DateTimeOffset(2026, 9, 17, 23, 58, 0, TimeSpan.FromHours(14));
        long ticks = 0;
        var service = CreateService(() => now, () => ticks);
        var changed = 0;
        service.Changed += snapshot => { if (snapshot.IsCompleted) changed++; };

        service.Start(null, 5);
        ticks = 300;
        now = now.AddMinutes(5);
        var completed = service.Snapshot();
        var repeated = service.Snapshot();

        Assert.True(completed.IsCompleted);
        Assert.True(repeated.IsCompleted);
        Assert.Equal(300, completed.ElapsedSeconds);
        Assert.Equal(1, changed);
        Assert.Equal(new FocusDailySummary(1, 300, 0), service.Summarize(new DateOnly(2026, 9, 18)));
    }

    [Fact]
    public void Restart_reconciles_running_wall_time_but_paused_time_does_not_advance()
    {
        var now = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.FromHours(8));
        var first = CreateService(() => now, () => 0);
        first.Start(Guid.NewGuid(), 25);

        now = now.AddMinutes(2);
        var restarted = CreateService(() => now, () => 0);
        Assert.Equal(120, restarted.Snapshot().ElapsedSeconds);
        restarted.Pause();

        now = now.AddHours(3);
        var pausedRestart = CreateService(() => now, () => 0);
        Assert.True(pausedRestart.Snapshot().IsPaused);
        Assert.Equal(120, pausedRestart.Snapshot().ElapsedSeconds);
    }

    [Fact]
    public void Completion_during_restart_is_exposed_once_for_user_feedback()
    {
        var now = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.FromHours(8));
        CreateService(() => now, () => 0).Start(null, 5);

        now = now.AddMinutes(6);
        var restarted = CreateService(() => now, () => 0);

        Assert.True(restarted.Snapshot().IsCompleted);
        Assert.True(restarted.ConsumeRecoveredCompletion());
        Assert.False(restarted.ConsumeRecoveredCompletion());
    }

    [Fact]
    public void Pause_at_duration_boundary_records_completion_instead_of_stuck_pause()
    {
        var now = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.FromHours(8));
        long ticks = 0;
        var service = CreateService(() => now, () => ticks);
        service.Start(null, 5);

        ticks = 300;
        var result = service.Pause();

        Assert.True(result.IsCompleted);
        Assert.False(result.IsActive);
        Assert.Equal(300, result.ElapsedSeconds);
    }

    [Fact]
    public void Large_clock_rollback_pauses_instead_of_guessing_elapsed_time()
    {
        var now = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.FromHours(8));
        CreateService(() => now, () => 0).Start(null, 25);

        now = now.AddMinutes(-10);
        var restarted = CreateService(() => now, () => 0);

        Assert.True(restarted.Snapshot().IsPaused);
        Assert.Equal(0, restarted.Snapshot().ElapsedSeconds);
    }

    [Fact]
    public void Corrupted_file_is_preserved_and_cannot_be_overwritten_by_empty_data()
    {
        var store = new FocusSessionStore(_root);
        File.WriteAllText(store.FocusSessionsPath, "{ broken");

        Assert.Empty(store.Load().Sessions);
        Assert.Throws<InvalidDataException>(() => store.Save(new FocusSessionDocument(), 0));
        Assert.Equal("{ broken", File.ReadAllText(store.FocusSessionsPath));
    }

    [Fact]
    public void Maintenance_validation_rejects_invalid_active_session_reference()
    {
        var json = """
        {"schemaVersion":1,"revision":0,"lastDurationMinutes":25,"activeSessionId":null,"sessions":[{"id":"11111111-1111-1111-1111-111111111111","todoId":null,"status":"Running","plannedSeconds":1500,"accumulatedSeconds":0,"startedAt":"2026-09-17T10:00:00+08:00","lastResumedAt":"2026-09-17T10:00:00+08:00","endedAt":null}]}
        """;

        Assert.Throws<InvalidDataException>(() => DataMaintenanceService.ValidateJson("focus-sessions.json", System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Maintenance_validation_rejects_missing_status_with_stable_data_error()
    {
        var json = """
        {"schemaVersion":1,"revision":0,"lastDurationMinutes":25,"activeSessionId":null,"sessions":[{"id":"11111111-1111-1111-1111-111111111111","todoId":null,"plannedSeconds":1500,"accumulatedSeconds":0,"startedAt":"2026-09-17T10:00:00+08:00","lastResumedAt":null,"endedAt":"2026-09-17T10:01:00+08:00"}]}
        """;

        Assert.Throws<InvalidDataException>(() => DataMaintenanceService.ValidateJson("focus-sessions.json", System.Text.Encoding.UTF8.GetBytes(json)));
    }

    private FocusSessionService CreateService(Func<DateTimeOffset> now, Func<long> timestamp) =>
        new(new FocusSessionStore(_root), now, timestamp, timestampFrequency: 1);
}
