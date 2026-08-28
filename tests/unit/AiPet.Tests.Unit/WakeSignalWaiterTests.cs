using System.Threading;
using AiPet.Common;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class WakeSignalWaiterTests
{
    [Fact]
    public void Returns_true_only_for_an_explicit_wake_signal()
    {
        using var wake = new AutoResetEvent(false);
        using var cancellation = new CancellationTokenSource();

        wake.Set();

        Assert.True(WakeSignalWaiter.Wait(wake, cancellation.Token));
    }

    [Fact]
    public void Cancellation_does_not_reopen_the_window()
    {
        using var wake = new AutoResetEvent(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(WakeSignalWaiter.Wait(wake, cancellation.Token));
    }
}
