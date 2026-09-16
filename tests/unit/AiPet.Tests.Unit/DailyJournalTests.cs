using System.Text;
using System.IO;
using AiPet.Storage;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class DailyJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aipet-journal-{Guid.NewGuid():N}");
    private DateTimeOffset _now = new(2026, 9, 14, 20, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Note_round_trip_uses_monotonic_revision_and_rejects_stale_write()
    {
        var store = new DailyJournalStore(_root, () => _now);
        var date = new DateOnly(2026, 9, 14);

        var first = store.SaveNote(date, "今天完成了本地搜索优化。", 0);
        _now = _now.AddMinutes(1);
        var second = store.SaveNote(date, "补充：回归测试通过。", first.Revision);

        Assert.Equal(2, second.Revision);
        Assert.Equal("补充：回归测试通过。", new DailyJournalStore(_root).Get(date)!.Note);
        Assert.Throws<DailyJournalConcurrencyException>(() => store.SaveNote(date, "过期写入", first.Revision));
    }

    [Fact]
    public void Corrupt_file_is_preserved_and_cannot_be_replaced_by_empty_data()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "journal.json");
        File.WriteAllText(path, "{ broken");
        var store = new DailyJournalStore(_root, () => _now);

        Assert.Empty(store.Load().Entries);
        Assert.Throws<InvalidDataException>(() => store.SaveNote(new DateOnly(2026, 9, 14), "不能覆盖", 0));
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public void Rollover_freezes_snapshot_once_and_activates_new_day()
    {
        var store = new DailyJournalStore(_root, () => _now);
        var oldDate = new DateOnly(2026, 9, 14);
        var newDate = oldDate.AddDays(1);
        store.Activate(oldDate);
        store.SaveNote(oldDate, "第一天", 0);
        var original = new[] { new DailyJournalSnapshotItem { Id = Guid.NewGuid(), Title = "已完成任务", IsCompleted = true } };

        var finalized = store.FinalizeAndActivate(oldDate, newDate, original);
        store.FinalizeAndActivate(oldDate, newDate, new[] { new DailyJournalSnapshotItem { Id = Guid.NewGuid(), Title = "不应覆盖" } });

        Assert.NotNull(finalized!.FinalizedAt);
        var reloaded = store.Load();
        Assert.Equal(newDate, reloaded.ActiveDate);
        Assert.Equal("已完成任务", Assert.Single(reloaded.Entries.Single().Snapshot).Title);
    }

    [Fact]
    public void Projection_classifies_related_tasks_without_modifying_todos()
    {
        var date = new DateOnly(2026, 9, 14);
        var todos = new[]
        {
            new TodoItem { Id = Guid.NewGuid(), Title = "完成", Status = TodoStatus.Completed, CompletedAt = _now },
            new TodoItem { Id = Guid.NewGuid(), Title = "安排", PlannedStartAt = _now.AddHours(-1), DueAt = _now.AddHours(1) },
            new TodoItem { Id = Guid.NewGuid(), Title = "无关", DueAt = _now.AddDays(2) },
        };

        var projection = DailyJournalProjector.Project(date, todos, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));

        Assert.Equal(1, projection.CompletedCount);
        Assert.Equal(1, projection.PendingCount);
        Assert.Equal(1, projection.PlannedCount);
        Assert.Equal(2, projection.Items.Count);
        Assert.Equal("无关", todos[2].Title);
    }

    [Fact]
    public void Markdown_export_is_utf8_without_bom_and_sanitizes_task_title_lines()
    {
        var date = new DateOnly(2026, 9, 14);
        var entry = new DailyJournalEntry
        {
            Date = date,
            UpdatedAt = _now,
            Note = "保持节奏。",
            Snapshot = new[]
            {
                new DailyJournalSnapshotItem { Id = Guid.NewGuid(), Title = "提交\r\n周报", IsCompleted = true, WasPlanned = true },
            },
        };
        var path = Path.Combine(_root, "exports", "journal.md");

        DailyJournalMarkdownExporter.Export(path, entry);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var markdown = Encoding.UTF8.GetString(bytes);
        Assert.Contains("# 每日复盘 · 2026-09-14", markdown, StringComparison.Ordinal);
        Assert.Contains("- [x] 提交 周报", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("提交\r\n周报", markdown, StringComparison.Ordinal);
        Assert.Throws<DailyJournalValidationException>(() => DailyJournalMarkdownExporter.Export(Path.Combine(_root, "journal.txt"), entry));

        var directoryTarget = Path.Combine(_root, "exports", "existing.md");
        Directory.CreateDirectory(directoryTarget);
        Assert.Throws<DailyJournalValidationException>(() => DailyJournalMarkdownExporter.Export(directoryTarget, entry));
        Assert.True(Directory.Exists(directoryTarget));
    }

    [Fact]
    public void Deleting_a_journal_never_deletes_its_source_todo()
    {
        var todos = new TodoStore(_root, () => _now);
        var todo = todos.Create(new TodoItem { Title = "保留待办" });
        var journal = new DailyJournalStore(_root, () => _now);
        var date = new DateOnly(2026, 9, 14);
        journal.SaveNote(date, "可删除", 0);

        Assert.True(journal.Delete(date));

        Assert.Null(journal.Get(date));
        Assert.Equal(todo.Id, Assert.Single(todos.Load()).Id);
    }

    [Fact]
    public void Maintenance_validation_requires_snapshot_flags_and_enforces_file_limit()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        {"schemaVersion":1,"activeDate":"2026-09-14","entries":[{"date":"2026-09-14","note":"","revision":1,"updatedAt":"2026-09-14T20:30:00+08:00","finalizedAt":null,"snapshot":[{"id":"{{id}}","title":"任务","relevantAt":null}]}]}
        """;

        Assert.Throws<InvalidDataException>(() => DataMaintenanceService.ValidateJson("journal.json", Encoding.UTF8.GetBytes(json)));
        Assert.Throws<InvalidDataException>(() => DataMaintenanceService.ValidateJson("journal.json", new byte[32 * 1024 * 1024 + 1]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
