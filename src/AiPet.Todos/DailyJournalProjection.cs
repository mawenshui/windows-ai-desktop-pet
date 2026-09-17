using System.Text;
using AiPet.Storage;

namespace AiPet.Todos;

public sealed record DailyJournalProjection(
    DateOnly Date,
    int CompletedCount,
    int PendingCount,
    int PlannedCount,
    IReadOnlyList<DailyJournalSnapshotItem> Items);

public static class DailyJournalProjector
{
    public static DailyJournalProjection Project(
        DateOnly date,
        IEnumerable<TodoItem> todos,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(todos);
        timeZone ??= TimeZoneInfo.Local;
        var rows = todos.Select(item =>
        {
            var completed = IsOnDate(item.CompletedAt, date, timeZone);
            var planned = IsOnDate(item.PlannedStartAt, date, timeZone);
            var due = IsOnDate(item.DueAt, date, timeZone);
            var reminder = IsOnDate(item.ReminderAt, date, timeZone);
            var relevant = completed ? item.CompletedAt
                : planned ? item.PlannedStartAt
                : due ? item.DueAt
                : reminder ? item.ReminderAt
                : null;
            return new { Item = item, Completed = completed, Planned = planned, Related = completed || planned || due || reminder, Relevant = relevant };
        })
        .Where(row => row.Related)
        .OrderBy(row => row.Relevant)
        .ThenBy(row => row.Item.Title, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(row => row.Item.Id)
        .Take(256)
        .Select(row => new DailyJournalSnapshotItem
        {
            Id = row.Item.Id,
            Title = row.Item.Title,
            IsCompleted = row.Completed,
            WasPlanned = row.Planned,
            RelevantAt = row.Relevant,
        })
        .ToArray();

        return new DailyJournalProjection(
            date,
            rows.Count(row => row.IsCompleted),
            rows.Count(row => !row.IsCompleted),
            rows.Count(row => row.WasPlanned),
            rows);
    }

    private static bool IsOnDate(DateTimeOffset? value, DateOnly date, TimeZoneInfo timeZone) =>
        value is { } timestamp
        && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, timeZone).DateTime) == date;
}

public static class DailyJournalMarkdownFormatter
{
    public static string Format(DailyJournalEntry entry, FocusDailySummary? focusSummary = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var completed = entry.Snapshot.Count(item => item.IsCompleted);
        var pending = entry.Snapshot.Count - completed;
        var planned = entry.Snapshot.Count(item => item.WasPlanned);
        var builder = new StringBuilder();
        builder.Append("# 每日复盘 · ").Append(entry.Date.ToString("yyyy-MM-dd")).Append('\n').Append('\n');
        builder.Append("- 已完成：").Append(completed).Append('\n');
        builder.Append("- 待处理：").Append(pending).Append('\n');
        builder.Append("- 已安排：").Append(planned).Append('\n').Append('\n');
        if (focusSummary is not null)
        {
            builder.Append("## 专注陪伴\n\n");
            builder.Append("- 完成次数：").Append(focusSummary.CompletedCount).Append('\n');
            builder.Append("- 专注时长：").Append(focusSummary.TotalSeconds / 60).Append(" 分钟\n");
            builder.Append("- 提前结束：").Append(focusSummary.EndedEarlyCount).Append(" 次\n\n");
        }
        builder.Append("## 相关事项\n\n");
        if (entry.Snapshot.Count == 0)
        {
            builder.Append("_当天没有相关待办。_\n");
        }
        else
        {
            foreach (var item in entry.Snapshot)
                builder.Append("- [").Append(item.IsCompleted ? 'x' : ' ').Append("] ")
                    .Append(Sanitize(item.Title)).Append('\n');
        }
        builder.Append("\n## 复盘记录\n\n");
        if (string.IsNullOrWhiteSpace(entry.Note))
        {
            builder.Append("_未填写。_\n");
        }
        else
        {
            var note = entry.Note.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            builder.Append(note);
            if (!note.EndsWith('\n')) builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(character => char.IsControl(character) ? ' ' : character).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public static class DailyJournalMarkdownExporter
{
    public static void Export(string path, DailyJournalEntry entry, FocusDailySummary? focusSummary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entry);
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetExtension(fullPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw new DailyJournalValidationException("复盘只能导出为 .md 文件。");
        if (fullPath.StartsWith("\\\\.\\", StringComparison.Ordinal)
            || fullPath.StartsWith("\\\\?\\GLOBALROOT", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(fullPath).Contains(':', StringComparison.Ordinal))
            throw new DailyJournalValidationException("不能导出到设备路径或 NTFS 数据流。");
        if (Directory.Exists(fullPath))
            throw new DailyJournalValidationException("复盘导出目标必须是文件。");
        RejectReparsePoints(fullPath);
        RecoverableAtomicFile.WriteAllText(fullPath, DailyJournalMarkdownFormatter.Format(entry, focusSummary));
    }

    private static void RejectReparsePoints(string fullPath)
    {
        for (var current = new FileInfo(fullPath).Directory; current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new DailyJournalValidationException("不能通过链接目录导出复盘。");
        }
        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new DailyJournalValidationException("不能覆盖链接文件。");
    }
}
