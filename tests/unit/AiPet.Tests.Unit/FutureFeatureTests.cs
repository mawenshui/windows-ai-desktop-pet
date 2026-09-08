using System.IO.Compression;
using System.IO;
using System.Text.Json;
using AiPet.AI;
using AiPet.Search;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class FutureFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-future-" + Guid.NewGuid().ToString("N"));
    public FutureFeatureTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Settings_schema_migration_preserves_pre_v3_and_pre_v4_backups()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"schemaVersion\":2,\"appearance\":{\"theme\":\"unknown\"}}");
        var loaded = new SettingsStore(_root).Load();
        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Equal("system", loaded.Appearance.Theme);
        Assert.True(File.Exists(Path.Combine(_root, "settings.json.pre-v3.bak")));
        Assert.True(File.Exists(Path.Combine(_root, "settings.json.pre-v4.bak")));
    }

    [Fact]
    public void Backup_and_restore_are_module_selective_and_exclude_credentials()
    {
        const string settings = "{\"schemaVersion\":3}";
        File.WriteAllText(Path.Combine(_root, "settings.json"), settings);
        File.WriteAllText(Path.Combine(_root, "todos.json"), "todos");
        var service = new DataMaintenanceService(_root);
        var zip = service.Backup(Path.Combine(_root, "backup.zip"), DataModule.Settings);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == "settings.json");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "todos.json");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("credential", StringComparison.OrdinalIgnoreCase));
        }
        File.WriteAllText(Path.Combine(_root, "settings.json"), "changed");
        var result = service.Restore(zip, DataModule.Settings);
        Assert.Empty(result.Errors);
        Assert.Equal(settings, File.ReadAllText(Path.Combine(_root, "settings.json")));
        Assert.Equal("todos", File.ReadAllText(Path.Combine(_root, "todos.json")));
    }

    [Fact]
    public void Diagnostic_export_uses_a_fixed_non_sensitive_contract()
    {
        var path = Path.Combine(_root, "diagnostic.json");
        DiagnosticExporter.Export(path, new DiagnosticSnapshot("0.11.0", "Windows", ".NET 8", "x64", "dark", 2, 3, 4, "custom", "Connected", new[] { "io_error" }));
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FullPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(11, DiagnosticExporter.ExportedFields.Count);
    }

    [Fact]
    public void Search_ranking_and_paging_are_stable_and_explainable()
    {
        using var index = new SearchIndex(Path.Combine(_root, "index.db"));
        var range = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        index.InsertItems(new[]
        {
            new SearchItemRow(range, "report", "c:\\report", "report", "", SearchItemKind.Document, 1, now.AddMinutes(-3)),
            new SearchItemRow(range, "report draft", "c:\\draft", "draft", "", SearchItemKind.Document, 1, now),
            new SearchItemRow(range, "annual report", "c:\\annual", "annual", "", SearchItemKind.Document, 1, now.AddMinutes(1)),
        });
        var first = index.Search("report", null, new SearchQueryOptions(Limit: 2));
        var second = index.Search("report", null, new SearchQueryOptions(Limit: 2, Offset: 2));
        Assert.Equal(new[] { "report", "report draft" }, first.Select(item => item.Name));
        Assert.Equal("annual report", Assert.Single(second).Name);
        Assert.Equal("名称完全匹配", first[0].MatchReason);
    }

    [Fact]
    public void Todo_supports_multiple_reminders_and_daily_recurrence()
    {
        var now = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(_root, () => now);
        var created = store.Create(new TodoItem
        {
            Title = "喝水", ReminderAt = now.AddMinutes(5),
            AdditionalReminderTimes = new[] { now.AddMinutes(10) },
            Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily },
        });
        var advanced = store.AdvanceReminder(created.Id, now.AddMinutes(5));
        Assert.Equal(now.AddMinutes(10), advanced.ReminderAt);
        advanced = store.AdvanceReminder(created.Id, now.AddMinutes(10));
        // Extra one-off reminders must not move the daily wall-clock anchor.
        Assert.Equal(now.AddMinutes(5).AddDays(1), advanced.ReminderAt);
        Assert.Equal(ReminderState.Scheduled, advanced.ReminderState);
    }

    [Fact]
    public void Shortcut_reorder_keeps_unspecified_items_and_cleans_only_orphans()
    {
        var store = new ShortcutStore(_root);
        var targetA = Path.Combine(_root, "a.txt"); var targetB = Path.Combine(_root, "b.txt");
        File.WriteAllText(targetA, "a"); File.WriteAllText(targetB, "b");
        var a = store.Add(new ShortcutItem { TargetPath = targetA, DisplayName = "a" });
        var b = store.Add(new ShortcutItem { TargetPath = targetB, DisplayName = "b" });
        store.Reorder(new[] { b.Id });
        Assert.Equal(new[] { b.Id, a.Id }, store.Load().Select(item => item.Id));
        Directory.CreateDirectory(store.IconsDir);
        var orphan = Path.Combine(store.IconsDir, "orphan.png"); File.WriteAllText(orphan, "x");
        Assert.Equal(1, store.CleanupUnreferencedIcons());
    }

    [Fact]
    public void Custom_provider_presets_require_safe_endpoints()
    {
        var good = Path.Combine(_root, "good.json");
        File.WriteAllText(good, "{\"schemaVersion\":1,\"providers\":[{\"id\":\"local\",\"displayName\":\"Local\",\"infoUrl\":\"\",\"defaultEndpoint\":\"http://127.0.0.1:8080/v1\",\"defaultModel\":\"test\",\"notes\":\"local\"}]}");
        Assert.Single(AiProviderPresetStore.Load(good));
        var bad = Path.Combine(_root, "bad.json");
        File.WriteAllText(bad, File.ReadAllText(good).Replace("http://127.0.0.1:8080/v1", "https://example.com/v1?token=secret"));
        Assert.Throws<InvalidDataException>(() => AiProviderPresetStore.Load(bad));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
