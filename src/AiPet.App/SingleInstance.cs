using System;
using System.IO;
using System.Threading;

namespace AiPet.App;

/// <summary>
/// Enforces single-instance behaviour via a local-scope named Mutex.
/// When a second instance is launched, it writes the existing instance's
/// process id (or a "wake" token) to a shared event and exits immediately.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\WindowsAiDesktopPet_v1";
    private const string WakeEventName = @"Local\WindowsAiDesktopPet_Wake_v1";

    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }
    public EventWaitHandle? WakeEvent { get; }

    public SingleInstance(string? isolatedScope = null)
    {
        var mutexName = isolatedScope is null ? MutexName : $@"Local\WindowsAiDesktopPet_{isolatedScope}_v1";
        var wakeEventName = isolatedScope is null ? WakeEventName : $@"Local\WindowsAiDesktopPet_Wake_{isolatedScope}_v1";
        _mutex = new Mutex(initiallyOwned: true, name: mutexName, out var createdNew);
        IsFirstInstance = createdNew;
        if (IsFirstInstance)
        {
            // Make sure the wake event exists for the first instance to listen on.
            WakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, wakeEventName, out _);
        }
    }

    /// <summary>
    /// Signal the wake event so the first instance can show the tool window.
    /// </summary>
    public static void SignalFirstInstance()
    {
        try
        {
            using var wh = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            wh.Set();
        }
        catch
        {
            // The first instance may not be running; ignore.
        }
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try { WakeEvent?.Dispose(); } catch { /* ignore */ }
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}
