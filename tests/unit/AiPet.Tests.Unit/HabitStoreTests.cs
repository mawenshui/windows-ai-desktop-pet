using System.IO;
using AiPet.Storage;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class HabitStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-habits-" + Guid.NewGuid().ToString("N"));
    public HabitStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void One_check_in_per_day_is_idempotent_and_disable_keeps_history()
    {
        var store = new HabitStore(_root);
        var today = new DateOnly(2026, 10, 20);
        var entry = store.Create("读书", new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(8)));
        store.SetChecked(entry.Id, today, true);
        store.SetChecked(entry.Id, today, true);
        Assert.Single(Assert.Single(store.Load().Entries).CheckIns);

        store.SetEnabled(entry.Id, false);
        Assert.Throws<InvalidDataException>(() => store.SetChecked(entry.Id, today.AddDays(1), true));
        Assert.Single(Assert.Single(new HabitStore(_root).Load().Entries).CheckIns);
        store.SetEnabled(entry.Id, true);
        store.SetChecked(entry.Id, today, false);
        Assert.Empty(Assert.Single(store.Load().Entries).CheckIns);
    }

    [Fact]
    public void Corrupt_file_is_kept_and_blocks_create_and_clear()
    {
        var store = new HabitStore(_root);
        File.WriteAllText(store.HabitsPath, "{ broken");
        Assert.Empty(store.Load().Entries);
        Assert.True(store.HasReadError);
        Assert.Throws<InvalidDataException>(() => store.Create("读书", DateTimeOffset.Now));
        Assert.Throws<InvalidDataException>(() => store.ClearAll());
        Assert.Equal("{ broken", File.ReadAllText(store.HabitsPath));
    }

    [Fact]
    public void Oversized_file_is_kept_and_blocks_all_writes()
    {
        var store = new HabitStore(_root);
        using (var stream = new FileStream(store.HabitsPath, FileMode.Create, FileAccess.Write))
            stream.SetLength(HabitStore.MaximumDocumentBytes + 1L);
        Assert.Empty(store.Load().Entries);
        Assert.True(store.HasReadError);
        Assert.Throws<InvalidDataException>(() => store.Create("读书", DateTimeOffset.Now));
        Assert.Throws<InvalidDataException>(() => store.ClearAll());
        Assert.Equal(HabitStore.MaximumDocumentBytes + 1L, new FileInfo(store.HabitsPath).Length);
    }

    [Fact]
    public void Backup_restore_and_clear_round_trip_habit_history()
    {
        var source = Path.Combine(_root, "source");
        var target = Path.Combine(_root, "target");
        var first = new HabitStore(source);
        var entry = first.Create("散步", new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.FromHours(8)));
        first.SetChecked(entry.Id, new DateOnly(2026, 10, 19), true);
        var archive = new DataMaintenanceService(source).Backup(Path.Combine(_root, "habits-backup.zip"), DataModule.Habits);

        var maintenance = new DataMaintenanceService(target);
        Assert.Contains(maintenance.PreviewRestore(archive), module => module.Module == DataModule.Habits && module.FileCount == 1);
        var result = maintenance.Restore(archive, DataModule.Habits);
        Assert.Empty(result.Errors);
        var restored = Assert.Single(new HabitStore(target).Load().Entries);
        Assert.Equal("散步", restored.Title);
        Assert.Equal(new DateOnly(2026, 10, 19), Assert.Single(restored.CheckIns));

        new HabitStore(target).ClearAll();
        Assert.Empty(new HabitStore(target).Load().Entries);
        Assert.Single(first.Load().Entries);
    }

    [Fact]
    public void Automatic_backup_includes_habits_by_default()
    {
        Assert.True(AutomaticBackupService.IncludedModules.HasFlag(DataModule.Habits));
        var source = Path.Combine(_root, "automatic");
        var habit = new HabitStore(source);
        habit.Create("散步", DateTimeOffset.Now);
        var result = new AutomaticBackupService(source).Run(enabled: true, retentionCount: 2, force: true);
        Assert.Equal(AutomaticBackupOutcome.Created, result.Outcome);
        Assert.Contains(new DataMaintenanceService(source).PreviewRestore(result.ArchivePath!),
            module => module.Module == DataModule.Habits && module.FileCount == 1);
    }

    [Fact]
    public void Restore_validation_rejects_duplicate_days()
    {
        var invalid = """
        {"schemaVersion":1,"revision":1,"entries":[{"id":"11111111-1111-1111-1111-111111111111","title":"读书","enabled":true,"createdAt":"2026-10-20T09:00:00+08:00","checkIns":["2026-10-20","2026-10-20"]}]}
        """;
        Assert.Throws<InvalidDataException>(() => DataMaintenanceService.ValidateJson("habits.json", System.Text.Encoding.UTF8.GetBytes(invalid)));
    }
}
