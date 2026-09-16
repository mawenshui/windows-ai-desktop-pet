using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.Todos;

public sealed class DailyJournalStore
{
    private const int MaxEntries = 3650;
    private const int MaxSnapshotItems = 256;
    private const int MaxDocumentBytes = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private bool _readFailed;

    public DailyJournalStore(string? overrideRoot = null, Func<DateTimeOffset>? now = null)
    {
        var root = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsAiDesktopPet");
        JournalPath = Path.Combine(root, "journal.json");
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string JournalPath { get; }

    public DailyJournalDocument Load()
    {
        lock (_gate) return Clone(ReadDocument());
    }

    public DailyJournalEntry? Get(DateOnly date)
    {
        lock (_gate)
        {
            return ReadDocument().Entries.FirstOrDefault(entry => entry.Date == date) is { } entry
                ? Clone(entry)
                : null;
        }
    }

    public DailyJournalEntry SaveNote(DateOnly date, string note, long expectedRevision)
    {
        note ??= string.Empty;
        ValidateNote(note);
        if (expectedRevision < 0) throw new DailyJournalValidationException("复盘版本无效。");

        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Entries.FindIndex(entry => entry.Date == date);
            var existing = index >= 0 ? document.Entries[index] : null;
            var actualRevision = existing?.Revision ?? 0;
            if (expectedRevision != actualRevision)
                throw new DailyJournalConcurrencyException("复盘内容已被其他保存更新，请重新载入后再试。");
            if (existing?.FinalizedAt is not null)
                throw new DailyJournalValidationException("历史复盘处于只读状态，请先明确启用编辑。");

            var saved = Normalize((existing ?? new DailyJournalEntry { Date = date }) with
            {
                Note = note,
                Revision = actualRevision + 1,
                UpdatedAt = _now(),
            });
            if (index < 0) document.Entries.Add(saved);
            else document.Entries[index] = saved;
            WriteDocument(document);
            return Clone(saved);
        }
    }

    public DailyJournalEntry SaveHistoricalNote(DateOnly date, string note, long expectedRevision)
    {
        note ??= string.Empty;
        ValidateNote(note);
        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Entries.FindIndex(entry => entry.Date == date);
            if (index < 0)
            {
                if (expectedRevision != 0)
                    throw new DailyJournalConcurrencyException("复盘内容已被其他保存更新，请重新载入后再试。");
                var created = Normalize(new DailyJournalEntry
                {
                    Date = date,
                    Note = note,
                    Revision = 1,
                    UpdatedAt = _now(),
                });
                document.Entries.Add(created);
                WriteDocument(document);
                return Clone(created);
            }
            var existing = document.Entries[index];
            if (expectedRevision != existing.Revision)
                throw new DailyJournalConcurrencyException("复盘内容已被其他保存更新，请重新载入后再试。");
            var saved = Normalize(existing with
            {
                Note = note,
                Revision = existing.Revision + 1,
                UpdatedAt = _now(),
            });
            document.Entries[index] = saved;
            WriteDocument(document);
            return Clone(saved);
        }
    }

    public void Activate(DateOnly date)
    {
        lock (_gate)
        {
            var document = ReadDocument();
            if (document.ActiveDate == date) return;
            WriteDocument(document with { ActiveDate = date });
        }
    }

    public DailyJournalEntry? FinalizeAndActivate(
        DateOnly dateToFinalize,
        DateOnly newActiveDate,
        IReadOnlyList<DailyJournalSnapshotItem> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Count > MaxSnapshotItems)
            throw new DailyJournalValidationException($"每日最多保存 {MaxSnapshotItems} 条待办快照。");

        lock (_gate)
        {
            var document = ReadDocument();
            var index = document.Entries.FindIndex(entry => entry.Date == dateToFinalize);
            DailyJournalEntry? finalized = null;
            if (index < 0)
            {
                finalized = Normalize(new DailyJournalEntry
                {
                    Date = dateToFinalize,
                    UpdatedAt = _now(),
                    FinalizedAt = _now(),
                    Snapshot = snapshot.Select(Clone).ToArray(),
                });
                document.Entries.Add(finalized);
            }
            else
            {
                var existing = document.Entries[index];
                if (existing.FinalizedAt is null)
                {
                    finalized = Normalize(existing with
                    {
                        FinalizedAt = _now(),
                        Snapshot = snapshot.Select(Clone).ToArray(),
                    });
                    document.Entries[index] = finalized;
                }
                else
                {
                    finalized = existing;
                }
            }

            WriteDocument(document with { ActiveDate = newActiveDate });
            return finalized is null ? null : Clone(finalized);
        }
    }

    public bool Delete(DateOnly date)
    {
        lock (_gate)
        {
            var document = ReadDocument();
            var removed = document.Entries.RemoveAll(entry => entry.Date == date) > 0;
            if (!removed) return false;
            WriteDocument(document);
            return true;
        }
    }

    private DailyJournalDocument ReadDocument()
    {
        _readFailed = false;
        try
        {
            if (!File.Exists(JournalPath)) return new DailyJournalDocument();
            var bytes = File.ReadAllBytes(JournalPath);
            if (bytes.Length > MaxDocumentBytes) throw new InvalidDataException("复盘文件超过 32 MiB 限制。");
            DataMaintenanceService.ValidateJson("journal.json", bytes);
            var document = JsonSerializer.Deserialize<DailyJournalDocument>(bytes, Options)
                ?? new DailyJournalDocument();
            ValidateDocument(document);
            return document;
        }
        catch
        {
            _readFailed = true;
            return new DailyJournalDocument();
        }
    }

    private void WriteDocument(DailyJournalDocument document)
    {
        if (_readFailed) throw new InvalidDataException("现有复盘无法读取，请先恢复备份，原文件保留。");
        ValidateDocument(document);
        document.Entries.Sort((left, right) => right.Date.CompareTo(left.Date));
        var json = JsonSerializer.Serialize(document, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
            throw new DailyJournalValidationException("复盘文件超过 32 MiB 限制。");
        DataMaintenanceService.ValidateJson("journal.json", Encoding.UTF8.GetBytes(json));
        RecoverableAtomicFile.WriteAllText(JournalPath, json);
    }

    private static void ValidateDocument(DailyJournalDocument document)
    {
        if (document.SchemaVersion != 1) throw new InvalidDataException("不支持的复盘数据版本。");
        if (document.Entries.Count > MaxEntries)
            throw new DailyJournalValidationException($"复盘记录不能超过 {MaxEntries} 天。");
        if (document.Entries.Select(entry => entry.Date).Distinct().Count() != document.Entries.Count)
            throw new InvalidDataException("复盘文件包含重复日期。");
        foreach (var entry in document.Entries) _ = Normalize(entry);
    }

    private static DailyJournalEntry Normalize(DailyJournalEntry entry)
    {
        ValidateNote(entry.Note ?? string.Empty);
        if (entry.Revision < 0) throw new DailyJournalValidationException("复盘版本无效。");
        if (entry.Snapshot.Count > MaxSnapshotItems)
            throw new DailyJournalValidationException($"每日最多保存 {MaxSnapshotItems} 条待办快照。");
        if (entry.Snapshot.Select(item => item.Id).Distinct().Count() != entry.Snapshot.Count)
            throw new DailyJournalValidationException("复盘快照包含重复待办。");

        return entry with
        {
            Note = entry.Note ?? string.Empty,
            Snapshot = entry.Snapshot.Select(item =>
            {
                var title = (item.Title ?? string.Empty).Trim();
                if (title.Length == 0 || title.Length > 200)
                    throw new DailyJournalValidationException("快照标题必须为 1 到 200 个字符。");
                return item with { Title = title };
            }).ToArray(),
        };
    }

    private static void ValidateNote(string note)
    {
        if (note.Length > 4000) throw new DailyJournalValidationException("复盘内容不能超过 4000 个字符。");
    }

    private static DailyJournalDocument Clone(DailyJournalDocument document) => document with
    {
        Entries = document.Entries.Select(Clone).ToList(),
    };

    private static DailyJournalEntry Clone(DailyJournalEntry entry) => entry with
    {
        Snapshot = entry.Snapshot.Select(Clone).ToArray(),
    };

    private static DailyJournalSnapshotItem Clone(DailyJournalSnapshotItem item) => item with { };
}
