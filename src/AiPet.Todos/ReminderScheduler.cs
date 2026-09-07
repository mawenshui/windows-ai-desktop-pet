namespace AiPet.Todos;

public sealed record ReminderNotification(TodoItem Item, bool IsRecovery);

public sealed class ReminderScheduler : IDisposable
{
    private readonly TodoStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private Timer? _timer;
    private Func<ReminderNotification, bool>? _deliver;
    private readonly bool _queuesNotifications;
    private volatile bool _disposed;

    /// <summary>
    /// Raised after a delivery has been accepted and the store has been
    /// updated.  UI hosts can refresh their list without racing the state
    /// transition performed by <see cref="CheckNowAsync"/>.
    /// </summary>
    public event Action<ReminderNotification>? Delivered;

    public ReminderScheduler(TodoStore store, Func<DateTimeOffset>? now = null, bool queuesNotifications = false)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _now = now ?? (() => DateTimeOffset.Now);
        _queuesNotifications = queuesNotifications;
        _store.Changed += Reschedule;
    }

    public void Start(
        Func<ReminderNotification, bool> deliver,
        TimeSpan? interval = null)
    {
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        var period = interval ?? TimeSpan.FromDays(24);
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _timer?.Dispose();
        _timer = new Timer(
            async _ =>
            {
                try { if (!_disposed) await CheckNowAsync(_deliver).ConfigureAwait(false); }
                catch { /* Persisted failed/unprocessed work remains available for diagnosis. */ }
                finally { if (!_disposed) ScheduleNext(period); }
            },
            null,
            TimeSpan.Zero,
            Timeout.InfiniteTimeSpan);
    }

    public void Reschedule() => ScheduleNext(TimeSpan.FromDays(24));

    private void ScheduleNext(TimeSpan maximumDelay)
    {
        if (_timer is null || _disposed) return;
        var next = _store.GetNextReminder();
        var delay = next is null ? Timeout.InfiniteTimeSpan : next.Value - _now();
        if (next is not null && delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        if (delay != Timeout.InfiniteTimeSpan && delay > maximumDelay) delay = maximumDelay;
        try { _timer.Change(delay, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
    }

    public async Task<int> CheckNowAsync(
        Func<ReminderNotification, bool> deliver,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return 0;
        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return 0;
        try
        {
            var now = _now();
            var dueItems = _store.Load()
                .Where(item => item.Status == TodoStatus.Pending)
                .Where(item => TodoStore.GetNextReminder(item) is { } time && time <= now)
                .Where(item => item.ReminderState is ReminderState.Scheduled or ReminderState.Snoozed)
                .OrderBy(TodoStore.GetNextReminder).ThenBy(item => item.Id)
                .ToArray();

            var deliveredCount = 0;
            foreach (var item in dueItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scheduledAt = TodoStore.GetNextReminder(item)!.Value;
                var notification = new ReminderNotification(
                    item,
                    now - scheduledAt > TimeSpan.FromMinutes(1));
                try
                {
                    var deliveredItem = _store.DeliverReminderIfCurrent(item.Id,scheduledAt,now,current => deliver(notification with {Item=current}),_queuesNotifications);
                    if (deliveredItem is not null)
                    {
                        try
                        {
                            Delivered?.Invoke(notification with { Item = deliveredItem });
                        }
                        catch
                        {
                            // A UI refresh listener must not turn a persisted
                            // delivery into a false failure state.
                        }
                        deliveredCount++;
                    }
                }
                catch
                {
                    // A failed durable write leaves the original occurrence recoverable.
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
        _disposed = true;
        _store.Changed -= Reschedule;
        _timer?.Dispose();
        // An in-flight check still owns the semaphore; do not dispose it under Release().
    }
    public async Task WaitForIdleAsync()
    {
        await _checkGate.WaitAsync().ConfigureAwait(false);
        _checkGate.Release();
    }
}
