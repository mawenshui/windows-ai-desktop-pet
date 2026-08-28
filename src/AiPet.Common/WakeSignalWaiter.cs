using System;
using System.Threading;

namespace AiPet.Common;

/// <summary>
/// Waits for an explicit single-instance wake signal without treating a
/// polling timeout as a request to reopen the tool window.
/// </summary>
public static class WakeSignalWaiter
{
    public static bool Wait(WaitHandle wakeSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wakeSignal);

        var signaledHandle = WaitHandle.WaitAny(
            [wakeSignal, cancellationToken.WaitHandle]);
        return signaledHandle == 0 && !cancellationToken.IsCancellationRequested;
    }
}
