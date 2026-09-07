using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.Todos;

public enum NotificationDeliveryState { Queued, Submitted, Handled, Cancelled }
public sealed record NotificationEntry(Guid Id, Guid TodoId, string Title, DateTimeOffset ScheduledAt,
    bool IsReminder, bool Bubble, bool Roam, bool IsRecovery, DateTimeOffset QueuedAt,
    NotificationDeliveryState State = NotificationDeliveryState.Queued, DateTimeOffset? SubmittedAt = null, DateTimeOffset? HandledAt = null)
{
    [JsonIgnore] public string StateText => State switch { NotificationDeliveryState.Queued=>"排队中",NotificationDeliveryState.Submitted=>"已提交 · 展示未确认",NotificationDeliveryState.Handled=>"已处理",_=>"已取消" };
    [JsonIgnore] public string TimeText => ScheduledAt.ToLocalTime().ToString("MM-dd HH:mm");
    [JsonIgnore] public bool CanHandle => TodoId != Guid.Empty && State is NotificationDeliveryState.Queued or NotificationDeliveryState.Submitted;
}
public sealed record QuietHours(bool Enabled=false, string Start="22:00", string End="08:00");
public sealed record NotificationDocument(int SchemaVersion=1, QuietHours? Quiet=null, List<NotificationEntry>? Entries=null, DateTimeOffset? NextSubmissionAt=null);

/// <summary>Durable inbox: accepting a request does not claim that Windows displayed it.</summary>
public sealed class NotificationCenter
{
    private static readonly JsonSerializerOptions Options = new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    private readonly object _gate=new();
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeZoneInfo> _zone;
    public string FilePath { get; }
    public NotificationCenter(string root,Func<DateTimeOffset>? now=null,Func<TimeZoneInfo>? zone=null)
    { FilePath=Path.Combine(root,"notifications.json"); _now=now??(()=>DateTimeOffset.Now); _zone=zone??(()=>TimeZoneInfo.Local); }
    private NotificationDocument Read()
    {
        if (!File.Exists(FilePath)) return new(Quiet:new(),Entries:new());
        if (new FileInfo(FilePath).Length > 4 * 1024 * 1024) throw new InvalidDataException("提醒记录超过容量上限。");
        var document=JsonSerializer.Deserialize<NotificationDocument>(File.ReadAllText(FilePath),Options)??throw new InvalidDataException("提醒记录无法读取。");
        if (document.SchemaVersion!=1 || document.Entries is null || document.Entries.Count>2000 ||
            document.Entries.Any(entry=>entry is null || entry.Id==Guid.Empty || string.IsNullOrWhiteSpace(entry.Title) || entry.Title.Length>200 || !Enum.IsDefined(entry.State)) ||
            document.Entries.Select(entry=>entry.Id).Distinct().Count()!=document.Entries.Count)
            throw new InvalidDataException("提醒记录格式不支持。");
        var quiet = document.Quiet ?? new();
        if (!TimeOnly.TryParseExact(quiet.Start,"HH:mm",out var start) || !TimeOnly.TryParseExact(quiet.End,"HH:mm",out var end) || (quiet.Enabled && start==end))
            throw new InvalidDataException("静默时段格式无效。");
        return document with { Quiet=document.Quiet??new() };
    }
    private void Write(NotificationDocument document)=>RecoverableAtomicFile.WriteAllText(FilePath,JsonSerializer.Serialize(document,Options));
    public IReadOnlyList<NotificationEntry> Entries { get { lock (_gate) return Read().Entries!.ToArray(); } }
    public QuietHours Quiet { get { lock (_gate) return Read().Quiet!; } }
    public bool IsQuiet { get { lock (_gate) return IsInQuietHours(Read().Quiet!,TimeZoneInfo.ConvertTime(_now(),_zone())); } }
    public static bool IsInQuietHours(QuietHours quiet,DateTimeOffset local)
    {
        if (!quiet.Enabled) return false;
        var start=TimeOnly.ParseExact(quiet.Start,"HH:mm"); var end=TimeOnly.ParseExact(quiet.End,"HH:mm"); var time=TimeOnly.FromDateTime(local.DateTime);
        return start<end ? time>=start && time<end : time>=start || time<end;
    }
    public void SetQuietHours(QuietHours quiet)
    {
        if (!TimeOnly.TryParseExact(quiet.Start,"HH:mm",out var start) || !TimeOnly.TryParseExact(quiet.End,"HH:mm",out var end) || (quiet.Enabled && start==end))
            throw new TodoValidationException("静默时段请使用 HH:mm，开始和结束不能相同。");
        lock (_gate) Write(Read() with {Quiet=quiet});
    }
    public bool Enqueue(ReminderNotification notification)
    {
        lock (_gate)
        {
            var document=Read(); var entries=document.Entries!; var item=notification.Item;
            var scheduled=TodoStore.GetNextReminder(item)??_now();
            if (entries.Any(entry=>entry.TodoId==item.Id && entry.ScheduledAt==scheduled)) return true;
            // Never discard queued items to make room for new work.
            if (entries.Count>=1000) entries.RemoveAll(entry=>entry.State is NotificationDeliveryState.Handled or NotificationDeliveryState.Cancelled && entry.QueuedAt<_now().AddDays(-7));
            if (entries.Count>=2000) return false;
            entries.Add(new(Guid.NewGuid(),item.Id,item.Title,scheduled,item.IsReminder,item.ReminderBubbleEnabled,item.ReminderRoamEnabled,notification.IsRecovery,_now()));
            Write(document); return true;
        }
    }
    public void EnqueueChannelTest()=>Enqueue(new(new TodoItem {Id=Guid.Empty,Title="提醒通道测试",ReminderAt=_now(),IsReminder=true,ReminderBubbleEnabled=true},false));
    public int SubmitNext(Func<IReadOnlyList<NotificationEntry>,bool> submit)
    {
        lock (_gate)
        {
            var document=Read(); var now=_now();
            if (IsInQuietHours(document.Quiet!,TimeZoneInfo.ConvertTime(now,_zone())) || document.NextSubmissionAt>now) return 0;
            var batch=document.Entries!.Where(entry=>entry.State==NotificationDeliveryState.Queued).OrderBy(entry=>entry.ScheduledAt).ToArray();
            if (batch.Length==0 || !submit(batch)) return 0;
            var ids=batch.Select(entry=>entry.Id).ToHashSet();
            Write(document with { Entries=document.Entries!.Select(entry=>ids.Contains(entry.Id)?entry with {State=NotificationDeliveryState.Submitted,SubmittedAt=now}:entry).ToList(),NextSubmissionAt=now.AddSeconds(30) });
            return batch.Length;
        }
    }
    public void Handle(Guid id) { lock (_gate) { var document=Read(); Write(document with {Entries=document.Entries!.Select(entry=>entry.Id==id?entry with {State=NotificationDeliveryState.Handled,HandledAt=_now()}:entry).ToList()}); } }
    public void CancelForTodo(Guid id) { lock (_gate) { var document=Read(); Write(document with {Entries=document.Entries!.Select(entry=>entry.TodoId==id && entry.State==NotificationDeliveryState.Queued?entry with {State=NotificationDeliveryState.Cancelled}:entry).ToList()}); } }
    public void HandleForTodo(Guid id) { lock (_gate) { var document=Read(); Write(document with {Entries=document.Entries!.Select(entry=>entry.TodoId==id && entry.CanHandle?entry with {State=NotificationDeliveryState.Handled,HandledAt=_now()}:entry).ToList()}); } }
    public void ClearHistory()
    {
        lock (_gate)
        {
            var document = Read();
            Write(document with { Entries = document.Entries!.Where(entry =>
                entry.State == NotificationDeliveryState.Queued ||
                (entry.State == NotificationDeliveryState.Submitted && entry.TodoId != Guid.Empty)).ToList() });
        }
    }
}
