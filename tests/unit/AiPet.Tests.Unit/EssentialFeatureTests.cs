using System;
using System.IO;
using System.IO.Compression;
using AiPet.Storage;
using AiPet.SystemIntegration;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class EssentialFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "aipet-essential-" + Guid.NewGuid().ToString("N"));

    public EssentialFeatureTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("control + alt + space", "Ctrl+Alt+Space")]
    [InlineData("win+shift+f12", "Shift+Win+F12")]
    [InlineData("Ctrl+7", "Ctrl+7")]
    public void Global_hotkey_parser_normalizes_supported_combinations(string input, string expected)
    {
        Assert.True(GlobalHotkeyGesture.TryParse(input, out var gesture, out var error), error);
        Assert.Equal(expected, gesture!.DisplayText);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl++A")]
    [InlineData("Ctrl+Alt+Enter")]
    [InlineData("Ctrl+Alt+Unknown")]
    public void Global_hotkey_parser_rejects_unsafe_or_incomplete_combinations(string input)
    {
        Assert.False(GlobalHotkeyGesture.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Global_hotkey_pair_must_be_distinct()
    {
        Assert.False(GlobalHotkeyGesture.TryParsePair(
            "Ctrl+Alt+Space", "Alt+Ctrl+Space", out _, out _, out var error));
        Assert.Contains("不能使用同一个", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Automatic_backup_is_daily_bounded_and_excludes_derived_or_sensitive_modules()
    {
        new SettingsStore(_root).Save(new AppSettings());
        File.WriteAllText(Path.Combine(_root, "index.db"), "derived-index");
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "aipet-debug.log"), "private-log");
        var now = DateTimeOffset.UtcNow;
        new AiPet.Todos.DailyJournalStore(_root, () => now)
            .SaveNote(DateOnly.FromDateTime(now.ToLocalTime().Date), "本地复盘", 0);
        var service = new AutomaticBackupService(_root, logs, () => now);

        var first = service.Run(enabled: true, retentionCount: 2);
        Assert.Equal(AutomaticBackupOutcome.Created, first.Outcome);
        Assert.NotNull(first.ArchivePath);

        now = now.AddHours(1);
        var sameDay = service.Run(enabled: true, retentionCount: 2);
        Assert.Equal(AutomaticBackupOutcome.Skipped, sameDay.Outcome);
        Assert.Equal(first.ArchivePath, sameDay.ArchivePath);

        for (var day = 1; day <= 3; day++)
        {
            now = now.AddDays(1);
            Assert.Equal(
                AutomaticBackupOutcome.Created,
                service.Run(enabled: true, retentionCount: 2, force: true).Outcome);
        }

        var archives = Directory.GetFiles(service.BackupDirectory, "aipet-auto-*.zip");
        Assert.Equal(2, archives.Length);
        using var zip = ZipFile.OpenRead(archives[0]);
        Assert.Contains(zip.Entries, entry => entry.FullName == "settings.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "journal.json");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName == "index.db");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_automatic_backup_preserves_existing_archives()
    {
        var store = new SettingsStore(_root);
        store.Save(new AppSettings());
        var service = new AutomaticBackupService(_root);
        Assert.Equal(AutomaticBackupOutcome.Created, service.Run(true, 7, force: true).Outcome);
        var before = Directory.GetFiles(service.BackupDirectory, "*.zip");

        File.WriteAllText(store.SettingsPath, "{broken-json");
        var failed = service.Run(true, 1, force: true);

        Assert.Equal(AutomaticBackupOutcome.Failed, failed.Outcome);
        Assert.Equal(before, Directory.GetFiles(service.BackupDirectory, "*.zip"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
