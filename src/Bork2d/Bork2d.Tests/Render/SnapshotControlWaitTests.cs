using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Handoff;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// Deterministic tests for the render thread's combined snapshot/control/stop wait (M7 §6.6, §9.1).
/// </summary>
/// <remarks>
/// Every wait is event-based and bounded; no test sleeps or polls. The test thread plays the
/// producer/host role and signals exactly one wake source per case.
/// </remarks>
public sealed class SnapshotControlWaitTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _notBlockedProbe = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void PublishWakesTheWaitWithASnapshot()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);
        var waiter = StartWait(wait);

        Assert.True(waiter.Entered.Wait(_timeout));
        Assert.False(waiter.Finished.Wait(_notBlockedProbe));

        Publish(ring, sequence: 1);

        Assert.True(waiter.Finished.Wait(_timeout));
        Assert.True(waiter.Thread.Join(_timeout));
        Assert.Equal(SnapshotWaitOutcome.Snapshot, waiter.Result[0]);
        Assert.True(ring.HasReadySnapshot);
    }

    [Fact]
    public void ControlPostWakesTheWaitWithControlWork()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);
        var waiter = StartWait(wait);

        Assert.True(waiter.Entered.Wait(_timeout));
        Assert.False(waiter.Finished.Wait(_notBlockedProbe));

        channel.PostExtent(new WindowExtent(800, 600));

        Assert.True(waiter.Finished.Wait(_timeout));
        Assert.True(waiter.Thread.Join(_timeout));
        Assert.Equal(SnapshotWaitOutcome.Control, waiter.Result[0]);
    }

    [Fact]
    public void ControlStopWakesTheWaitAsStopped()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);
        var waiter = StartWait(wait);

        Assert.True(waiter.Entered.Wait(_timeout));
        Assert.False(waiter.Finished.Wait(_notBlockedProbe));

        channel.RequestStop();

        Assert.True(waiter.Finished.Wait(_timeout));
        Assert.True(waiter.Thread.Join(_timeout));
        Assert.Equal(SnapshotWaitOutcome.Stopped, waiter.Result[0]);
    }

    [Fact]
    public void RingStopWakesTheWaitAsStopped()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);
        var waiter = StartWait(wait);

        Assert.True(waiter.Entered.Wait(_timeout));
        Assert.False(waiter.Finished.Wait(_notBlockedProbe));

        ring.Stop();

        Assert.True(waiter.Finished.Wait(_timeout));
        Assert.True(waiter.Thread.Join(_timeout));
        Assert.Equal(SnapshotWaitOutcome.Stopped, waiter.Result[0]);
    }

    [Fact]
    public void RendererFatalWakesTheWaitAsStopped()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);
        var waiter = StartWait(wait);

        Assert.True(waiter.Entered.Wait(_timeout));
        Assert.False(waiter.Finished.Wait(_notBlockedProbe));

        // The renderer maps a fatal to a ring stop (Q9=I1) before publishing it.
        ring.Stop();
        channel.PublishFatal(new Bork2dException("device lost"));

        Assert.True(waiter.Finished.Wait(_timeout));
        Assert.True(waiter.Thread.Join(_timeout));
        Assert.Equal(SnapshotWaitOutcome.Stopped, waiter.Result[0]);
    }

    [Fact]
    public void WaitReturnsImmediatelyWhenWorkIsAlreadyPending()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);

        Publish(ring, sequence: 1);
        Assert.Equal(SnapshotWaitOutcome.Snapshot, wait.Wait());

        var taken = ring.TakeLatest();
        Assert.NotNull(taken);
        ring.Release(taken!);

        channel.PostMinimized(true);
        Assert.Equal(SnapshotWaitOutcome.Control, wait.Wait());
        Assert.True(channel.TryDequeue(out _));

        ring.Stop();
        Assert.Equal(SnapshotWaitOutcome.Stopped, wait.Wait());
    }

    [Fact]
    public void SnapshotIsReportedBeforeControlWork()
    {
        using var ring = new SnapshotRing();
        using var channel = new RendererControlChannel();
        var wait = new SnapshotControlWait(ring, channel);

        Publish(ring, sequence: 1);
        channel.PostMinimized(true);

        // The snapshot wins so the renderer records exactly one frame for it; the control message
        // stays pending and is drained on the next loop iteration.
        Assert.Equal(SnapshotWaitOutcome.Snapshot, wait.Wait());
        Assert.True(channel.HasPendingWork);
    }

    private static (Thread Thread, ManualResetEventSlim Entered, ManualResetEventSlim Finished, SnapshotWaitOutcome[] Result) StartWait(
        SnapshotControlWait wait)
    {
        var entered = new ManualResetEventSlim();
        var finished = new ManualResetEventSlim();
        var result = new SnapshotWaitOutcome[1];
        var thread = new Thread(() =>
        {
            entered.Set();
            result[0] = wait.Wait();
            finished.Set();
        })
        {
            IsBackground = true,
            Name = "Bork2d.SnapshotControlWaitTest",
        };

        thread.Start();
        return (thread, entered, finished, result);
    }

    private static void Publish(SnapshotRing ring, long sequence)
    {
        SnapshotSlot? slot = ring.Acquire();
        Assert.NotNull(slot);
        slot!.Snapshot.Reset();
        slot.Snapshot.Sequence = sequence;
        ring.Publish(slot);
    }
}
