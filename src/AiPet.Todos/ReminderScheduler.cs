namespace AiPet.Todos;

public sealed record ReminderNotification(TodoItem Item, bool IsRecovery);

public sealed class ReminderScheduler : IDisposable
{
    private readonly TodoStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private Timer? _timer;
    private Func<ReminderNotification, bool>? _deliver;

    public ReminderScheduler(TodoStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public void Start(
        Func<ReminderNotification, bool> deliver,
        TimeSpan? interval = null)
    {
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        var period = interval ?? TimeSpan.FromSeconds(15);
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _timer?.Dispose();
        _timer = new Timer(
            async _ => await CheckNowAsync(_deliver).ConfigureAwait(false),
            null,
            TimeSpan.Zero,
            period);
    }

    public async Task<int> CheckNowAsync(
        Func<ReminderNotification, bool> deliver,
        CancellationToken cancellationToken = default)
    {
        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return 0;
        try
        {
            var now = _now();
            var dueItems = _store.Load()
                .Where(item => item.Status == TodoStatus.Pending)
                .Where(item => item.ReminderAt is not null && item.ReminderAt <= now)
                .Where(item => item.ReminderState is ReminderState.Scheduled or ReminderState.Snoozed)
                .OrderBy(item => item.ReminderAt)
                .ToArray();

            var deliveredCount = 0;
            foreach (var item in dueItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scheduledAt = item.ReminderAt!.Value;
                var notification = new ReminderNotification(
                    item,
                    now - scheduledAt > TimeSpan.FromMinutes(1));
                try
                {
                    if (deliver(notification))
                    {
                        _store.MarkReminderDelivered(item.Id, now);
                        deliveredCount++;
                    }
                    else
                    {
                        _store.MarkReminderFailed(item.Id, now, "NotificationRejected");
                    }
                }
                catch
                {
                    _store.MarkReminderFailed(item.Id, now, "NotificationException");
                }
            }
            return deliveredCount;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _checkGate.Dispose();
    }
}
