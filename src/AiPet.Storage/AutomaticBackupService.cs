using System.Globalization;

namespace AiPet.Storage;

public enum AutomaticBackupOutcome { Disabled, Skipped, Created, Failed }

public sealed record AutomaticBackupResult(
    AutomaticBackupOutcome Outcome,
    string Message,
    string Directory,
    string? ArchivePath = null,
    DateTimeOffset? LatestBackupAt = null);

/// <summary>Creates bounded, local-only snapshots by reusing the validated maintenance archive.</summary>
public sealed class AutomaticBackupService
{
    public const int DefaultRetentionCount = 7;
    public static readonly DataModule IncludedModules = DataModule.Settings | DataModule.Layout |
        DataModule.Todos | DataModule.Shortcuts | DataModule.Notifications | DataModule.ProviderPresets;

    private readonly string _root;
    private readonly string? _logDirectory;
    private readonly Func<DateTimeOffset> _now;
    public string BackupDirectory { get; }

    public AutomaticBackupService(string root, string? logDirectory = null, Func<DateTimeOffset>? now = null)
    {
        _root = Path.GetFullPath(root);
        _logDirectory = logDirectory;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        BackupDirectory = Path.Combine(_root, "automatic-backups");
    }

    public AutomaticBackupResult Run(bool enabled, int retentionCount, bool force = false)
    {
        retentionCount = Math.Clamp(retentionCount, 1, 30);
        if (!enabled) return new(AutomaticBackupOutcome.Disabled, "每日自动备份已关闭。", BackupDirectory);
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            RejectReparsePoint(BackupDirectory);
            var existing = ListArchives();
            var latest = existing.FirstOrDefault();
            var now = _now().ToUniversalTime();
            if (!force && latest is not null && now - latest.Timestamp < TimeSpan.FromHours(24))
                return new(AutomaticBackupOutcome.Skipped,
                    $"今日已有有效备份 · {latest.Timestamp.ToLocalTime():MM-dd HH:mm}",
                    BackupDirectory, latest.Path, latest.Timestamp);

            var destination = Path.Combine(BackupDirectory, $"aipet-auto-{now:yyyyMMddTHHmmssfffZ}.zip");
            var archive = new DataMaintenanceService(_root, _logDirectory).Backup(destination, IncludedModules);
            var current = new Archive(archive, now);
            var afterSuccess = ListArchives();
            foreach (var old in afterSuccess.Skip(retentionCount)) File.Delete(old.Path);
            return new(AutomaticBackupOutcome.Created,
                $"本地备份完成 · {now.ToLocalTime():MM-dd HH:mm} · 保留 {retentionCount} 份",
                BackupDirectory, current.Path, current.Timestamp);
        }
        catch
        {
            return new(AutomaticBackupOutcome.Failed,
                "自动备份未完成；原数据和已有备份均保留。",
                BackupDirectory);
        }
    }

    public AutomaticBackupResult GetStatus(bool enabled)
    {
        if (!enabled) return new(AutomaticBackupOutcome.Disabled, "每日自动备份已关闭。", BackupDirectory);
        try
        {
            var latest = ListArchives().FirstOrDefault();
            return latest is null
                ? new(AutomaticBackupOutcome.Skipped, "尚无自动备份；可立即创建。", BackupDirectory)
                : new(AutomaticBackupOutcome.Skipped, $"最近备份 · {latest.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}", BackupDirectory, latest.Path, latest.Timestamp);
        }
        catch
        {
            return new(AutomaticBackupOutcome.Failed, "自动备份目录无法读取。", BackupDirectory);
        }
    }

    private IReadOnlyList<Archive> ListArchives()
    {
        if (!Directory.Exists(BackupDirectory)) return Array.Empty<Archive>();
        RejectReparsePoint(BackupDirectory);
        return Directory.EnumerateFiles(BackupDirectory, "aipet-auto-*.zip", SearchOption.TopDirectoryOnly)
            .Where(path => !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            .Select(TryReadArchive)
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.Timestamp)
            .ThenByDescending(item => item.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static Archive? TryReadArchive(string path)
    {
        const string prefix = "aipet-auto-";
        var filename = Path.GetFileNameWithoutExtension(path);
        if (!filename.StartsWith(prefix, StringComparison.Ordinal)
            || !DateTimeOffset.TryParseExact(
                filename[prefix.Length..],
                "yyyyMMdd'T'HHmmssfff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return null;
        return new Archive(path, timestamp);
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("自动备份目录不能是链接或重解析点。");
    }

    private sealed record Archive(string Path, DateTimeOffset Timestamp);
}
