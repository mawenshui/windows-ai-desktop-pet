using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.Todos;

public enum FocusSessionStatus
{
    Running,
    Paused,
    Completed,
    EndedEarly,
}

public sealed record FocusSession
{
    [JsonPropertyName("id")] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonPropertyName("todoId")] public Guid? TodoId { get; init; }
    [JsonPropertyName("status")] public FocusSessionStatus Status { get; init; }
    [JsonPropertyName("plannedSeconds")] public int PlannedSeconds { get; init; } = 1500;
    [JsonPropertyName("accumulatedSeconds")] public int AccumulatedSeconds { get; init; }
    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("lastResumedAt")] public DateTimeOffset? LastResumedAt { get; init; }
    [JsonPropertyName("endedAt")] public DateTimeOffset? EndedAt { get; init; }
}

public sealed record FocusSessionDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("revision")] public long Revision { get; init; }
    [JsonPropertyName("lastDurationMinutes")] public int LastDurationMinutes { get; init; } = 25;
    [JsonPropertyName("activeSessionId")] public Guid? ActiveSessionId { get; init; }
    [JsonPropertyName("sessions")] public List<FocusSession> Sessions { get; init; } = new();
}

public sealed record FocusSessionSnapshot(
    FocusSession? Session,
    int ElapsedSeconds,
    int RemainingSeconds,
    long Revision)
{
    public bool IsRunning => Session?.Status == FocusSessionStatus.Running;
    public bool IsPaused => Session?.Status == FocusSessionStatus.Paused;
    public bool IsActive => IsRunning || IsPaused;
    public bool IsCompleted => Session?.Status == FocusSessionStatus.Completed;
    public bool IsEndedEarly => Session?.Status == FocusSessionStatus.EndedEarly;
}

public sealed record FocusDailySummary(int CompletedCount, int TotalSeconds, int EndedEarlyCount);

public sealed class FocusSessionValidationException : Exception
{
    public FocusSessionValidationException(string message) : base(message) { }
}

public sealed class FocusSessionConcurrencyException : Exception
{
    public FocusSessionConcurrencyException(string message) : base(message) { }
}

public sealed class FocusSessionStore
{
    public const int MaximumSessions = 10_000;
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private bool _readFailed;

    public FocusSessionStore(string? overrideRoot = null)
    {
        var root = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsAiDesktopPet");
        FocusSessionsPath = Path.Combine(root, "focus-sessions.json");
    }

    public string FocusSessionsPath { get; }

    public FocusSessionDocument Load()
    {
        lock (_gate) return Clone(ReadDocument());
    }

    public FocusSessionDocument Save(FocusSessionDocument candidate, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            var current = ReadDocument();
            if (current.Revision != expectedRevision)
                throw new FocusSessionConcurrencyException("专注记录已被其他操作更新，请重新载入。");
            var saved = candidate with { Revision = expectedRevision + 1 };
            WriteDocument(saved);
            return Clone(saved);
        }
    }

    private FocusSessionDocument ReadDocument()
    {
        _readFailed = false;
        try
        {
            if (!File.Exists(FocusSessionsPath)) return new FocusSessionDocument();
            var bytes = File.ReadAllBytes(FocusSessionsPath);
            if (bytes.Length > MaximumDocumentBytes)
                throw new InvalidDataException("专注记录超过 16 MiB 上限。");
            DataMaintenanceService.ValidateJson("focus-sessions.json", bytes);
            var document = JsonSerializer.Deserialize<FocusSessionDocument>(bytes, Options)
                ?? new FocusSessionDocument();
            Validate(document);
            return document;
        }
        catch
        {
            _readFailed = true;
            return new FocusSessionDocument();
        }
    }

    private void WriteDocument(FocusSessionDocument document)
    {
        if (_readFailed)
            throw new InvalidDataException("现有专注记录无法读取，请先恢复备份；原文件已保留。");
        Validate(document);
        var json = JsonSerializer.Serialize(document, Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumDocumentBytes)
            throw new FocusSessionValidationException("专注记录超过 16 MiB 上限。");
        DataMaintenanceService.ValidateJson("focus-sessions.json", bytes);
        RecoverableAtomicFile.WriteAllText(FocusSessionsPath, json);
    }

    internal static void Validate(FocusSessionDocument document)
    {
        if (document.SchemaVersion != 1) throw new InvalidDataException("不支持的专注记录版本。");
        if (document.Revision < 0) throw new InvalidDataException("专注记录 revision 无效。");
        if (document.LastDurationMinutes is < 5 or > 180)
            throw new FocusSessionValidationException("最近专注时长必须为 5 到 180 分钟。");
        if (document.Sessions.Count > MaximumSessions)
            throw new FocusSessionValidationException($"专注记录不能超过 {MaximumSessions} 条。");
        if (document.Sessions.Select(session => session.Id).Distinct().Count() != document.Sessions.Count)
            throw new InvalidDataException("专注记录包含重复标识。");

        foreach (var session in document.Sessions)
        {
            if (session.Id == Guid.Empty) throw new InvalidDataException("专注会话标识无效。");
            if (session.TodoId == Guid.Empty) throw new InvalidDataException("专注关联待办标识无效。");
            if (!Enum.IsDefined(session.Status)) throw new InvalidDataException("专注状态无效。");
            if (session.PlannedSeconds is < 300 or > 10_800)
                throw new FocusSessionValidationException("计划专注时长必须为 5 到 180 分钟。");
            if (session.AccumulatedSeconds < 0 || session.AccumulatedSeconds > session.PlannedSeconds)
                throw new InvalidDataException("专注累计时长无效。");
            if (session.StartedAt == default) throw new InvalidDataException("专注开始时间无效。");
            if (session.Status == FocusSessionStatus.Running && session.LastResumedAt is null)
                throw new InvalidDataException("运行中的专注缺少恢复时间。");
            if (session.Status is FocusSessionStatus.Completed or FocusSessionStatus.EndedEarly && session.EndedAt is null)
                throw new InvalidDataException("已结束专注缺少结束时间。");
            if (session.Status is FocusSessionStatus.Running or FocusSessionStatus.Paused && session.EndedAt is not null)
                throw new InvalidDataException("活动专注不能包含结束时间。");
        }

        var active = document.Sessions.Where(session => session.Status is FocusSessionStatus.Running or FocusSessionStatus.Paused).ToArray();
        if (active.Length > 1) throw new InvalidDataException("同时只能有一个活动专注会话。");
        if (active.Length == 0 && document.ActiveSessionId is not null)
            throw new InvalidDataException("活动会话引用无效。");
        if (active.Length == 1 && document.ActiveSessionId != active[0].Id)
            throw new InvalidDataException("活动会话引用与状态不一致。");
    }

    private static FocusSessionDocument Clone(FocusSessionDocument document) => document with
    {
        Sessions = document.Sessions.Select(session => session with { }).ToList(),
    };
}

/// <summary>
/// Owns focus transitions. A monotonic clock drives the active in-process
/// segment; wall-clock time is used only when resuming after process exit or
/// sleep. State transitions are persisted, while one-second UI ticks are not.
/// </summary>
public sealed class FocusSessionService
{
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly FocusSessionStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<long> _timestamp;
    private readonly double _timestampFrequency;
    private FocusSessionDocument _document;
    private long _segmentStartedTimestamp;
    private int _segmentBaseSeconds;
    private bool _recoveredCompletionPending;

    public FocusSessionService(
        FocusSessionStore store,
        Func<DateTimeOffset>? now = null,
        Func<long>? timestamp = null,
        double? timestampFrequency = null)
    {
        _store = store;
        _now = now ?? (() => DateTimeOffset.Now);
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _timestampFrequency = timestampFrequency ?? Stopwatch.Frequency;
        if (_timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        _document = _store.Load();
        ReconcileAfterLoad();
    }

    public event Action<FocusSessionSnapshot>? Changed;

    public int LastDurationMinutes
    {
        get { lock (_gate) return _document.LastDurationMinutes; }
    }

    public bool ConsumeRecoveredCompletion()
    {
        lock (_gate)
        {
            var pending = _recoveredCompletionPending;
            _recoveredCompletionPending = false;
            return pending;
        }
    }

    public FocusSessionSnapshot Snapshot()
    {
        lock (_gate)
        {
            CompleteIfElapsed();
            return BuildSnapshot();
        }
    }

    public FocusSessionSnapshot Start(Guid? todoId, int durationMinutes)
    {
        if (todoId == Guid.Empty) throw new FocusSessionValidationException("关联待办标识无效。");
        if (durationMinutes is < 5 or > 180)
            throw new FocusSessionValidationException("专注时长必须为 5 到 180 分钟。");
        lock (_gate)
        {
            if (ActiveSession() is not null)
                throw new FocusSessionValidationException("已有专注会话正在运行或暂停。");
            if (_document.Sessions.Count >= FocusSessionStore.MaximumSessions)
                throw new FocusSessionValidationException("专注记录已达上限，请先备份并清理历史记录。");
            var now = _now();
            var session = new FocusSession
            {
                Id = Guid.NewGuid(),
                TodoId = todoId,
                Status = FocusSessionStatus.Running,
                PlannedSeconds = checked(durationMinutes * 60),
                AccumulatedSeconds = 0,
                StartedAt = now,
                LastResumedAt = now,
            };
            var sessions = _document.Sessions.Select(item => item with { }).ToList();
            sessions.Add(session);
            Save(_document with
            {
                LastDurationMinutes = durationMinutes,
                ActiveSessionId = session.Id,
                Sessions = sessions,
            });
            StartMonotonicSegment(session.AccumulatedSeconds);
            return Publish();
        }
    }

    public FocusSessionSnapshot Pause()
    {
        lock (_gate)
        {
            var session = RequireActive(FocusSessionStatus.Running);
            var elapsed = CurrentElapsed(session);
            if (elapsed >= session.PlannedSeconds)
            {
                Complete(session, elapsed);
                return Publish();
            }
            ReplaceAndSave(session with
            {
                Status = FocusSessionStatus.Paused,
                AccumulatedSeconds = elapsed,
                LastResumedAt = null,
            });
            return Publish();
        }
    }

    public FocusSessionSnapshot Resume()
    {
        lock (_gate)
        {
            var session = RequireActive(FocusSessionStatus.Paused);
            if (session.AccumulatedSeconds >= session.PlannedSeconds)
            {
                Complete(session, session.AccumulatedSeconds);
                return Publish();
            }
            var resumed = session with
            {
                Status = FocusSessionStatus.Running,
                LastResumedAt = _now(),
            };
            ReplaceAndSave(resumed);
            StartMonotonicSegment(resumed.AccumulatedSeconds);
            return Publish();
        }
    }

    public FocusSessionSnapshot EndEarly()
    {
        lock (_gate)
        {
            var session = ActiveSession()
                ?? throw new FocusSessionValidationException("当前没有活动专注会话。");
            var elapsed = session.Status == FocusSessionStatus.Running
                ? CurrentElapsed(session)
                : session.AccumulatedSeconds;
            if (elapsed >= session.PlannedSeconds)
            {
                Complete(session, elapsed);
                return Publish();
            }
            ReplaceAndSave(session with
            {
                Status = FocusSessionStatus.EndedEarly,
                AccumulatedSeconds = elapsed,
                LastResumedAt = null,
                EndedAt = _now(),
            }, clearActive: true);
            return Publish();
        }
    }

    public FocusSessionSnapshot Checkpoint()
    {
        lock (_gate)
        {
            var session = ActiveSession();
            if (session?.Status != FocusSessionStatus.Running) return BuildSnapshot();
            var elapsed = CurrentElapsed(session);
            if (elapsed >= session.PlannedSeconds)
            {
                Complete(session, elapsed);
                return Publish();
            }
            var checkpointed = session with
            {
                AccumulatedSeconds = elapsed,
                LastResumedAt = _now(),
            };
            ReplaceAndSave(checkpointed);
            StartMonotonicSegment(checkpointed.AccumulatedSeconds);
            return Publish();
        }
    }

    public FocusSessionSnapshot ReconcileWallClock()
    {
        lock (_gate)
        {
            var session = ActiveSession();
            if (session?.Status != FocusSessionStatus.Running) return BuildSnapshot();
            var now = _now();
            var last = session.LastResumedAt ?? session.StartedAt;
            if (last - now > ClockRollbackTolerance)
            {
                ReplaceAndSave(session with { Status = FocusSessionStatus.Paused, LastResumedAt = null });
                return Publish();
            }
            var wallSeconds = Math.Max(0, (int)Math.Floor((now - last).TotalSeconds));
            var elapsed = Math.Min(session.PlannedSeconds, Math.Max(
                CurrentElapsed(session),
                session.AccumulatedSeconds + wallSeconds));
            if (elapsed >= session.PlannedSeconds)
            {
                Complete(session, elapsed);
                return Publish();
            }
            var reconciled = session with { AccumulatedSeconds = elapsed, LastResumedAt = now };
            ReplaceAndSave(reconciled);
            StartMonotonicSegment(reconciled.AccumulatedSeconds);
            return Publish();
        }
    }

    public FocusDailySummary Summarize(DateOnly date, TimeZoneInfo? timeZone = null)
    {
        lock (_gate)
        {
            var sessions = _document.Sessions.Where(session =>
                session.EndedAt is { } ended
                && DateOnly.FromDateTime(timeZone is null
                    ? ended.DateTime
                    : TimeZoneInfo.ConvertTime(ended, timeZone).DateTime) == date).ToArray();
            return new FocusDailySummary(
                sessions.Count(session => session.Status == FocusSessionStatus.Completed),
                sessions.Where(session => session.Status is FocusSessionStatus.Completed or FocusSessionStatus.EndedEarly)
                    .Sum(session => session.AccumulatedSeconds),
                sessions.Count(session => session.Status == FocusSessionStatus.EndedEarly));
        }
    }

    private void ReconcileAfterLoad()
    {
        lock (_gate)
        {
            var session = ActiveSession();
            if (session?.Status != FocusSessionStatus.Running)
            {
                if (session is not null) StartMonotonicSegment(session.AccumulatedSeconds);
                return;
            }
            var now = _now();
            var last = session.LastResumedAt ?? session.StartedAt;
            if (last - now > ClockRollbackTolerance)
            {
                ReplaceAndSave(session with { Status = FocusSessionStatus.Paused, LastResumedAt = null });
                return;
            }
            var wallSeconds = Math.Max(0, (int)Math.Floor((now - last).TotalSeconds));
            var elapsed = Math.Min(session.PlannedSeconds, session.AccumulatedSeconds + wallSeconds);
            if (elapsed >= session.PlannedSeconds)
            {
                Complete(session, elapsed);
                _recoveredCompletionPending = true;
                return;
            }
            var resumed = session with { AccumulatedSeconds = elapsed, LastResumedAt = now };
            ReplaceAndSave(resumed);
            StartMonotonicSegment(resumed.AccumulatedSeconds);
        }
    }

    private void CompleteIfElapsed()
    {
        var session = ActiveSession();
        if (session?.Status != FocusSessionStatus.Running) return;
        var elapsed = CurrentElapsed(session);
        if (elapsed >= session.PlannedSeconds)
        {
            Complete(session, elapsed);
            Publish();
        }
    }

    private void Complete(FocusSession session, int elapsed) => ReplaceAndSave(session with
    {
        Status = FocusSessionStatus.Completed,
        AccumulatedSeconds = Math.Min(session.PlannedSeconds, elapsed),
        LastResumedAt = null,
        EndedAt = _now(),
    }, clearActive: true);

    private int CurrentElapsed(FocusSession session)
    {
        if (session.Status != FocusSessionStatus.Running) return session.AccumulatedSeconds;
        var ticks = Math.Max(0, _timestamp() - _segmentStartedTimestamp);
        var seconds = (int)Math.Floor(ticks / _timestampFrequency);
        return Math.Min(session.PlannedSeconds, _segmentBaseSeconds + seconds);
    }

    private FocusSession? ActiveSession() => _document.ActiveSessionId is { } id
        ? _document.Sessions.FirstOrDefault(session => session.Id == id)
        : null;

    private FocusSession RequireActive(FocusSessionStatus status)
    {
        var session = ActiveSession();
        if (session?.Status != status)
            throw new FocusSessionValidationException(status == FocusSessionStatus.Running
                ? "当前没有正在运行的专注会话。"
                : "当前没有已暂停的专注会话。");
        return session;
    }

    private void ReplaceAndSave(FocusSession replacement, bool clearActive = false)
    {
        var sessions = _document.Sessions.Select(session => session.Id == replacement.Id ? replacement : session).ToList();
        Save(_document with
        {
            ActiveSessionId = clearActive ? null : replacement.Id,
            Sessions = sessions,
        });
    }

    private void Save(FocusSessionDocument candidate) =>
        _document = _store.Save(candidate, _document.Revision);

    private void StartMonotonicSegment(int accumulatedSeconds)
    {
        _segmentBaseSeconds = accumulatedSeconds;
        _segmentStartedTimestamp = _timestamp();
    }

    private FocusSessionSnapshot BuildSnapshot()
    {
        var active = ActiveSession();
        var current = active ?? _document.Sessions.LastOrDefault();
        var elapsed = current is null ? 0 : CurrentElapsed(current);
        return new FocusSessionSnapshot(
            current,
            elapsed,
            current is null ? 0 : Math.Max(0, current.PlannedSeconds - elapsed),
            _document.Revision);
    }

    private FocusSessionSnapshot Publish()
    {
        var snapshot = BuildSnapshot();
        Changed?.Invoke(snapshot);
        return snapshot;
    }
}
