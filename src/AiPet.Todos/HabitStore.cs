using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.Todos;

public sealed record HabitEntry
{
    [JsonPropertyName("id")] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("checkIns")] public List<DateOnly> CheckIns { get; init; } = new();
}

public sealed record HabitDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("revision")] public long Revision { get; init; }
    [JsonPropertyName("entries")] public List<HabitEntry> Entries { get; init; } = new();
}

/// <summary>Manual, local-only check-ins. Calendar days never auto-complete or penalize a missed day.</summary>
public sealed class HabitStore
{
    public const int MaximumHabits = 100;
    public const int MaximumCheckInsPerHabit = 3650;
    public const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly object _gate = new();
    private bool _readFailed;

    public HabitStore(string? overrideRoot = null)
    {
        var root = overrideRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsAiDesktopPet");
        HabitsPath = Path.Combine(root, "habits.json");
    }

    public string HabitsPath { get; }
    public bool HasReadError { get { lock (_gate) return _readFailed; } }

    public HabitDocument Load()
    {
        lock (_gate) return Clone(Read());
    }

    public HabitEntry Create(string title, DateTimeOffset now)
    {
        title = NormalizeTitle(title);
        lock (_gate)
        {
            var document = Read();
            if (document.Entries.Count >= MaximumHabits) throw new InvalidDataException("最多保存 100 个习惯。");
            var entry = new HabitEntry { Title = title, CreatedAt = now };
            document.Entries.Add(entry);
            Write(document);
            return entry;
        }
    }

    public HabitEntry SetChecked(Guid id, DateOnly date, bool isChecked)
    {
        lock (_gate)
        {
            var document = Read();
            var index = document.Entries.FindIndex(entry => entry.Id == id);
            if (index < 0) throw new InvalidDataException("习惯已不存在，请刷新清单。");
            var entry = document.Entries[index];
            if (!entry.Enabled) throw new InvalidDataException("习惯已停用，请先启用。");
            var dates = entry.CheckIns.ToList();
            if (isChecked && !dates.Contains(date))
            {
                if (dates.Count >= MaximumCheckInsPerHabit) throw new InvalidDataException("该习惯的历史记录已达上限。");
                dates.Add(date);
                dates.Sort();
            }
            else if (!isChecked) dates.Remove(date);
            else return entry;
            entry = entry with { CheckIns = dates };
            document.Entries[index] = entry;
            Write(document);
            return entry;
        }
    }

    public HabitEntry SetEnabled(Guid id, bool enabled)
    {
        lock (_gate)
        {
            var document = Read();
            var index = document.Entries.FindIndex(entry => entry.Id == id);
            if (index < 0) throw new InvalidDataException("习惯已不存在，请刷新清单。");
            var entry = document.Entries[index];
            if (entry.Enabled == enabled) return entry;
            entry = entry with { Enabled = enabled };
            document.Entries[index] = entry;
            Write(document);
            return entry;
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            var document = Read();
            if (_readFailed) throw new InvalidDataException("习惯记录无法读取；原文件已保留，请先恢复备份。");
            if (document.Entries.Count == 0) return;
            Write(document with { Entries = new List<HabitEntry>() });
        }
    }

    private HabitDocument Read()
    {
        _readFailed = false;
        try
        {
            if (!File.Exists(HabitsPath)) return new HabitDocument();
            using var stream = new FileStream(HabitsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumDocumentBytes) throw new InvalidDataException("习惯记录超过容量上限。");
            using var buffer = new MemoryStream((int)stream.Length);
            var chunk = new byte[81920];
            int count;
            while ((count = stream.Read(chunk)) > 0)
            {
                if (buffer.Length + count > MaximumDocumentBytes) throw new InvalidDataException("习惯记录超过容量上限。");
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            DataMaintenanceService.ValidateJson("habits.json", bytes);
            var document = JsonSerializer.Deserialize<HabitDocument>(bytes, Options) ?? throw new InvalidDataException("习惯记录为空。");
            Validate(document);
            return document;
        }
        catch
        {
            _readFailed = true;
            return new HabitDocument();
        }
    }

    private void Write(HabitDocument document)
    {
        if (_readFailed) throw new InvalidDataException("习惯记录无法读取；原文件已保留，请先恢复备份。");
        document = document with { Revision = checked(document.Revision + 1) };
        Validate(document);
        var json = JsonSerializer.Serialize(document, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes) throw new InvalidDataException("习惯记录超过容量上限。");
        DataMaintenanceService.ValidateJson("habits.json", Encoding.UTF8.GetBytes(json));
        RecoverableAtomicFile.WriteAllText(HabitsPath, json);
    }

    private static string NormalizeTitle(string? title)
    {
        var value = title?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 80 || value.Any(char.IsControl)) throw new ArgumentException("习惯名称请输入 1 到 80 个可见字符。", nameof(title));
        return value;
    }

    private static void Validate(HabitDocument document)
    {
        if (document.SchemaVersion != 1 || document.Revision < 0 || document.Entries.Count > MaximumHabits)
            throw new InvalidDataException("习惯记录版本或数量无效。");
        var ids = new HashSet<Guid>();
        foreach (var entry in document.Entries)
        {
            if (entry.Id == Guid.Empty || !ids.Add(entry.Id) || entry.Title is null || entry.Title.Length is < 1 or > 80 || entry.Title.Any(char.IsControl)
                || entry.CheckIns is null || entry.CheckIns.Count > MaximumCheckInsPerHabit || entry.CheckIns.Count != entry.CheckIns.Distinct().Count())
                throw new InvalidDataException("习惯记录内容无效。");
        }
    }

    private static HabitDocument Clone(HabitDocument document) => document with
    {
        Entries = document.Entries.Select(entry => entry with { CheckIns = entry.CheckIns.ToList() }).ToList(),
    };
}
