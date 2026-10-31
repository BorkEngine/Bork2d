using Bork2d.Render.Handoff;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// Deterministic state-machine tests for the three-slot snapshot ring (D-003, M7 §6.4, §9.1).
/// </summary>
/// <remarks>
/// Every wait is event-based and bounded; no test sleeps or polls. The eight slot invariants are
/// asserted directly or through <see cref="SnapshotRingAssertions"/>.
/// </remarks>
public sealed class SnapshotRingTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _notBlockedProbe = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void NewRingHasThreeFreeSlotsAndNoLatest()
    {
        using var ring = new SnapshotRing();

        Assert.Equal(3, SnapshotRing.SlotCapacity);
        Assert.Equal(3, ring.Slots.Length);
        Assert.Equal(-1, ring.LatestIndex);
        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(default(SnapshotRingCounters), ring.Counters);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void AcquireMovesAFreeSlotToWritingAndAbandonReturnsIt()
    {
        using var ring = new SnapshotRing();

        var slot = Acquire(ring);
        Assert.Equal(SnapshotSlotState.Writing, slot.State);
        Assert.Equal(0, ring.Counters.BackpressureWaits);

        ring.Abandon(slot);

        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(0, ring.Counters.Published);
    }

    [Fact]
    public void AcquireNeverWaitsWhileAnySlotIsFree()
    {
        using var ring = new SnapshotRing();

        for (var index = 0; index < 4; index++)
        {
            var slot = Acquire(ring);
            ring.Publish(slot);
        }

        Assert.Equal(0, ring.Counters.BackpressureWaits);
        Assert.True(ring.LatestIndex >= 0);
    }

    [Fact]
    public void PublishMovesWritingToReadyAndBecomesLatest()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);
        slot.Snapshot.Reset();
        slot.Snapshot.Sequence = 42;

        ring.Publish(slot);

        Assert.Equal(SnapshotSlotState.Ready, slot.State);
        Assert.Equal(slot.Index, ring.LatestIndex);
        Assert.Equal(1, ring.Counters.Published);
        Assert.Equal(0, ring.Counters.Superseded);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void PublishSupersedesOnlyThePreviousReadySlot()
    {
        using var ring = new SnapshotRing();
        var first = Acquire(ring);
        ring.Publish(first);

        var second = Acquire(ring);
        ring.Publish(second);

        Assert.Equal(SnapshotSlotState.Free, first.State);
        Assert.Equal(SnapshotSlotState.Ready, second.State);
        Assert.Equal(second.Index, ring.LatestIndex);
        Assert.Equal(2, ring.Counters.Published);
        Assert.Equal(1, ring.Counters.Superseded);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void PublishNeverFreesARenderingSlot()
    {
        using var ring = new SnapshotRing();
        var held = Acquire(ring);
        ring.Publish(held);
        var rendering = Take(ring);

        var second = Acquire(ring);
        ring.Publish(second);
        var third = Acquire(ring);
        ring.Publish(third);

        Assert.Equal(SnapshotSlotState.Rendering, rendering.State);
        Assert.Equal(SnapshotSlotState.Free, second.State);
        Assert.Equal(SnapshotSlotState.Ready, third.State);
        Assert.Equal(1, ring.Counters.Superseded);
        SnapshotRingAssertions.AssertQuiescent(ring);

        // The newest Ready snapshot is taken; publishing over a Rendering latest never frees it.
        var takenThird = Take(ring);
        var fourth = Acquire(ring);
        ring.Publish(fourth);

        Assert.Equal(SnapshotSlotState.Rendering, takenThird.State);
        Assert.Equal(SnapshotSlotState.Rendering, rendering.State);
        Assert.Equal(SnapshotSlotState.Ready, fourth.State);
        Assert.Equal(1, ring.Counters.Superseded);
        Assert.False(SnapshotRingAssertions.HasFreeSlot(ring));
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void TakeLatestMovesReadyToRenderingAndClearsLatest()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);
        ring.Publish(slot);

        var taken = Take(ring);

        Assert.Same(slot, taken);
        Assert.Equal(SnapshotSlotState.Rendering, taken.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(1, ring.Counters.Consumed);
        Assert.Null(ring.TakeLatest());
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void TakeLatestReturnsNullWithoutAReadySnapshot()
    {
        using var ring = new SnapshotRing();

        Assert.Null(ring.TakeLatest());
        Assert.Equal(0, ring.Counters.Consumed);
    }

    [Fact]
    public void DiscardLatestFreesTheNewestReadyWithoutConsumingIt()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);
        ring.Publish(slot);

        Assert.True(ring.DiscardLatest());

        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(0, ring.Counters.Consumed);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
        Assert.False(ring.DiscardLatest());
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void DiscardLatestNeverFreesARenderingOrWritingSlot()
    {
        using var ring = new SnapshotRing();
        var published = Acquire(ring);
        ring.Publish(published);
        var rendering = Take(ring);

        Assert.False(ring.DiscardLatest());
        Assert.Equal(SnapshotSlotState.Rendering, rendering.State);

        var writing = Acquire(ring);
        Assert.False(ring.DiscardLatest());
        Assert.Equal(SnapshotSlotState.Writing, writing.State);
        ring.Abandon(writing);
        ring.Release(rendering);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void ReleaseMovesRenderingToFree()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);
        ring.Publish(slot);
        var taken = Take(ring);

        ring.Release(taken);

        Assert.Equal(SnapshotSlotState.Free, taken.State);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void FilledSnapshotValuesSurvivePublicationAndTaking()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);
        slot.Snapshot.Reset();
        slot.Snapshot.Sequence = 1234;
        slot.Snapshot.Time = 1.25;
        slot.Snapshot.DeltaTime = 0.5;
        slot.Snapshot.AddSprite(default);

        ring.Publish(slot);
        var taken = Take(ring);

        Assert.Equal(1234, taken.Snapshot.Sequence);
        Assert.Equal(1.25, taken.Snapshot.Time);
        Assert.Equal(0.5, taken.Snapshot.DeltaTime);
        Assert.Equal(1, taken.Snapshot.SpriteCount);
    }

    [Fact]
    public void AcquireBlocksWhileNoSlotIsFreeAndAFenceReleaseWakesIt()
    {
        using var ring = new SnapshotRing();
        var filled = FillRing(ring);
        Assert.False(SnapshotRingAssertions.HasFreeSlot(ring));

        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new SnapshotSlot?[1];
        var producer = StartProducer(ring, started, completed, result);

        Assert.True(started.Wait(_timeout));
        Assert.False(completed.Wait(_notBlockedProbe), "Acquire returned a slot while the ring was full.");

        ring.Release(filled.First);

        Assert.True(completed.Wait(_timeout));
        Assert.True(producer.Join(_timeout));
        Assert.NotNull(result[0]);
        Assert.Equal(SnapshotSlotState.Writing, result[0]!.State);
        Assert.True(ring.Counters.BackpressureWaits >= 1);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void StopWakesABlockedProducerAndAcquireStaysStopped()
    {
        using var ring = new SnapshotRing();
        FillRing(ring);

        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new SnapshotSlot?[1];
        var producer = StartProducer(ring, started, completed, result);

        Assert.True(started.Wait(_timeout));
        Assert.False(completed.Wait(_notBlockedProbe));

        ring.Stop();

        Assert.True(completed.Wait(_timeout));
        Assert.True(producer.Join(_timeout));
        Assert.Null(result[0]);
        Assert.Equal(1, ring.Counters.StopWakes);
        Assert.Null(ring.Acquire());
        Assert.Equal(2, ring.Counters.StopWakes);
        Assert.False(ring.WaitForSnapshotOrStop());
    }

    [Fact]
    public void StopWakesABlockedConsumer()
    {
        using var ring = new SnapshotRing();
        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new bool[1];
        var consumer = StartConsumer(ring, started, completed, result);

        Assert.True(started.Wait(_timeout));
        Assert.False(completed.Wait(_notBlockedProbe));

        ring.Stop();

        Assert.True(completed.Wait(_timeout));
        Assert.True(consumer.Join(_timeout));
        Assert.False(result[0]);
    }

    [Fact]
    public void WaitForSnapshotOrStopWakesOnPublish()
    {
        using var ring = new SnapshotRing();
        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new bool[1];
        var consumer = StartConsumer(ring, started, completed, result);

        Assert.True(started.Wait(_timeout));
        var slot = Acquire(ring);
        ring.Publish(slot);

        Assert.True(completed.Wait(_timeout));
        Assert.True(consumer.Join(_timeout));
        Assert.True(result[0]);

        var taken = Take(ring);
        ring.Release(taken);
    }

    [Fact]
    public void SignalExitWakesABlockedAcquire()
    {
        using var ring = new SnapshotRing();
        FillRing(ring);

        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new SnapshotSlot?[1];
        var producer = StartProducer(ring, started, completed, result);

        Assert.True(started.Wait(_timeout));
        Assert.False(completed.Wait(_notBlockedProbe));

        ring.SignalExit();

        Assert.True(completed.Wait(_timeout));
        Assert.True(producer.Join(_timeout));
        Assert.Null(result[0]);
        Assert.True(ring.Counters.ExitWakes >= 1);
        Assert.Null(ring.Acquire());
    }

    [Fact]
    public void SignalExitStillAllowsConsumptionAndTheFinalExitTickPublish()
    {
        using var ring = new SnapshotRing();
        var finalSlot = Acquire(ring);

        ring.SignalExit();

        Assert.Null(ring.Acquire());
        Assert.True(ring.Counters.ExitWakes >= 1);

        finalSlot.Snapshot.Reset();
        finalSlot.Snapshot.Sequence = 99;
        ring.Publish(finalSlot);
        Assert.Equal(SnapshotSlotState.Ready, finalSlot.State);

        Assert.True(ring.WaitForSnapshotOrStop());
        var taken = Take(ring);
        Assert.Equal(99, taken.Snapshot.Sequence);
        ring.Release(taken);

        Assert.Equal(1, ring.Counters.Consumed);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void StopDoesNotForceFreeAWritingSlotAndPublishStaysLegal()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);

        ring.Stop();
        Assert.Equal(SnapshotSlotState.Writing, slot.State);

        ring.Publish(slot);
        Assert.Equal(SnapshotSlotState.Ready, slot.State);

        ring.DrainAfterRenderThreadJoined();
        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, ring.LatestIndex);
    }

    [Fact]
    public void DrainReleasesEveryNonWritingSlotAndClearsLatest()
    {
        using var ring = new SnapshotRing();
        var filled = FillRing(ring);
        Assert.Equal(3, SnapshotRingAssertions.LiveCount(ring));
        Assert.Equal(filled.Ready.Index, ring.LatestIndex);

        ring.DrainAfterRenderThreadJoined();

        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, ring.LatestIndex);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void MaxLiveNeverExceedsThreeAndTracksReadyPlusRendering()
    {
        using var ring = new SnapshotRing();
        FillRing(ring);

        Assert.Equal(3, SnapshotRingAssertions.LiveCount(ring));
        Assert.Equal(3, ring.Counters.MaxLive);
    }

    [Fact]
    public void MaxLiveCountsTheTransientReadyBeforeSupersession()
    {
        using var ring = new SnapshotRing();
        var held = Acquire(ring);
        ring.Publish(held);
        var rendering = Take(ring);
        Assert.Equal(SnapshotSlotState.Rendering, rendering.State);

        var oldReady = Acquire(ring);
        ring.Publish(oldReady);

        // The new slot is Ready before the old Ready is freed: 1 Rendering + 2 Ready = 3 live.
        var newReady = Acquire(ring);
        ring.Publish(newReady);

        Assert.Equal(SnapshotSlotState.Rendering, rendering.State);
        Assert.Equal(SnapshotSlotState.Free, oldReady.State);
        Assert.Equal(SnapshotSlotState.Ready, newReady.State);
        Assert.Equal(2, SnapshotRingAssertions.LiveCount(ring));
        Assert.Equal(3, ring.Counters.MaxLive);
    }

    [Fact]
    public void CountersAccountForPublishedConsumedAndSupersededSnapshots()
    {
        using var ring = new SnapshotRing();

        var first = Acquire(ring);
        ring.Publish(first);
        var second = Acquire(ring);
        ring.Publish(second);
        var taken = Take(ring);
        ring.Release(taken);

        var counters = ring.Counters;
        Assert.Equal(2, counters.Published);
        Assert.Equal(1, counters.Consumed);
        Assert.Equal(1, counters.Superseded);

        // The second publish transiently holds the old Ready and the new Ready, so the live
        // high-water mark is two even though the stable live count is one.
        Assert.Equal(2, counters.MaxLive);
    }

    [Fact]
    public void InvalidTransitionsAreRejected()
    {
        using var ring = new SnapshotRing();
        var slot = Acquire(ring);

        Assert.Throws<InvalidOperationException>(() => ring.Release(slot));
        ring.Publish(slot);
        Assert.Throws<InvalidOperationException>(() => ring.Publish(slot));
        Assert.Throws<InvalidOperationException>(() => ring.Release(slot));
        Assert.Throws<InvalidOperationException>(() => ring.Abandon(slot));
        Assert.Throws<ArgumentNullException>(() => ring.Publish(null!));
        Assert.Throws<ArgumentNullException>(() => ring.Release(null!));
        Assert.Throws<ArgumentNullException>(() => ring.Abandon(null!));
    }

    private static SnapshotSlot Acquire(SnapshotRing ring)
    {
        var slot = ring.Acquire();
        Assert.NotNull(slot);
        return slot!;
    }

    private static SnapshotSlot Take(SnapshotRing ring)
    {
        var slot = ring.TakeLatest();
        Assert.NotNull(slot);
        return slot!;
    }

    private static (SnapshotSlot First, SnapshotSlot Second, SnapshotSlot Ready) FillRing(SnapshotRing ring)
    {
        var first = Acquire(ring);
        ring.Publish(first);
        var firstRendering = Take(ring);

        var second = Acquire(ring);
        ring.Publish(second);
        var secondRendering = Take(ring);

        var ready = Acquire(ring);
        ring.Publish(ready);

        Assert.Equal(SnapshotSlotState.Rendering, firstRendering.State);
        Assert.Equal(SnapshotSlotState.Rendering, secondRendering.State);
        Assert.Equal(SnapshotSlotState.Ready, ready.State);
        Assert.Equal(ready.Index, ring.LatestIndex);
        Assert.False(SnapshotRingAssertions.HasFreeSlot(ring));
        return (firstRendering, secondRendering, ready);
    }

    private static Thread StartProducer(
        SnapshotRing ring,
        ManualResetEventSlim started,
        ManualResetEventSlim completed,
        SnapshotSlot?[] result)
    {
        var thread = new Thread(() =>
        {
            started.Set();
            result[0] = ring.Acquire();
            completed.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return thread;
    }

    private static Thread StartConsumer(
        SnapshotRing ring,
        ManualResetEventSlim started,
        ManualResetEventSlim completed,
        bool[] result)
    {
        var thread = new Thread(() =>
        {
            started.Set();
            result[0] = ring.WaitForSnapshotOrStop();
            completed.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return thread;
    }
}
