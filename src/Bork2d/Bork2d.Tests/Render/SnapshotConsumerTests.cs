using Bork2d.Render.Handoff;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// Deterministic tests for the renderer-side snapshot consumer: one frame per consumed snapshot, a
/// slot held until its frame is released, release on frame reuse, the suspended drain, and the
/// stop/idle release (Q7=G1, Q8=H1, §9.1).
/// </summary>
/// <remarks>
/// No GPU, no Vulkan, no sleeps. The test drives the "frame fence observed" step directly by
/// calling <see cref="SnapshotConsumer.TakeForFreeFrame"/> again for the same frame index, which is
/// exactly what the renderer does after its fence wait.
/// </remarks>
public sealed class SnapshotConsumerTests
{
    [Fact]
    public void NewConsumerHasNoSnapshotAndNoFramesInFlight()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        Assert.Same(ring, consumer.Ring);
        Assert.False(consumer.HasReadySnapshot);
        Assert.False(consumer.IsStopped);
        Assert.Equal(0, consumer.SuspendedConsumptions);
        Assert.Equal(default(SnapshotRingCounters), consumer.Counters);
        Assert.Null(consumer.TakeForFreeFrame(0));
        Assert.Equal(0, consumer.Counters.Consumed);
    }

    [Fact]
    public void EachConsumedSnapshotIsBoundToOneFrameAndCountedOnce()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot first = Publish(ring, sequence: 1);
        SnapshotSlot takenFirst = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, takenFirst);
        Assert.Same(first, takenFirst);
        Assert.Equal(1, consumer.Counters.Consumed);
        Assert.Equal(SnapshotSlotState.Rendering, first.State);

        // No new snapshot: the free frame takes nothing, so no GPU frame is recorded.
        Assert.Null(consumer.TakeForFreeFrame(1));
        Assert.Equal(1, consumer.Counters.Consumed);

        SnapshotSlot second = Publish(ring, sequence: 2);
        SnapshotSlot taken = Take(consumer, frameIndex: 1);
        consumer.AttachToFrame(1, taken);

        Assert.Same(second, taken);
        Assert.Equal(2, consumer.Counters.Consumed);
        Assert.Equal(2, consumer.Counters.Published);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void SlotStaysRenderingUntilItsFrameIsReused()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot first = Publish(ring, sequence: 1);
        SnapshotSlot takenFirst = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, takenFirst);
        Assert.Same(first, takenFirst);

        SnapshotSlot second = Publish(ring, sequence: 2);
        SnapshotSlot takenSecond = Take(consumer, frameIndex: 1);
        consumer.AttachToFrame(1, takenSecond);
        Assert.Same(second, takenSecond);

        SnapshotSlot third = Publish(ring, sequence: 3);

        // Both GPU frames are in flight: the new snapshot stays Ready and the two held slots stay
        // Rendering until their frames are reused.
        Assert.Equal(SnapshotSlotState.Rendering, first.State);
        Assert.Equal(SnapshotSlotState.Rendering, second.State);
        Assert.Equal(SnapshotSlotState.Ready, third.State);
        Assert.Equal(third.Index, ring.LatestIndex);

        // Reusing frame 0 observes its fence: the prior slot is released and the newest is taken.
        SnapshotSlot taken = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, taken);

        Assert.Same(third, taken);
        Assert.Equal(SnapshotSlotState.Free, first.State);
        Assert.Equal(SnapshotSlotState.Rendering, second.State);
        Assert.Equal(SnapshotSlotState.Rendering, third.State);
        Assert.Equal(3, consumer.Counters.Consumed);
    }

    [Fact]
    public void ReleaseTakenFreesASlotThatNeverBecameAGpuFrame()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot slot = Publish(ring, sequence: 1);
        SnapshotSlot taken = Take(consumer, frameIndex: 0);
        Assert.Same(slot, taken);

        consumer.ReleaseTaken(taken);

        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(1, consumer.Counters.Consumed);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
    }

    [Fact]
    public void ReleaseAllFreesEveryHeldAndTakenSlotAfterIdle()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        Publish(ring, sequence: 1);
        SnapshotSlot first = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, first);

        Publish(ring, sequence: 2);
        SnapshotSlot second = Take(consumer, frameIndex: 1);
        consumer.AttachToFrame(1, second);

        Publish(ring, sequence: 3);
        SnapshotSlot pending = Take(consumer, frameIndex: 0);

        Assert.Equal(SnapshotSlotState.Free, first.State);
        Assert.Equal(SnapshotSlotState.Rendering, second.State);
        Assert.Equal(SnapshotSlotState.Rendering, pending.State);

        consumer.ReleaseAll();

        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void SuspendedDrainFreesTheNewestSnapshotWithoutEnteringRendering()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot slot = Publish(ring, sequence: 1);

        Assert.True(consumer.DrainSuspended());

        // The slot went straight Ready -> Free: it was never consumed for a GPU frame.
        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(0, consumer.Counters.Consumed);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
        Assert.Equal(1, consumer.SuspendedConsumptions);
        Assert.False(consumer.DrainSuspended());
        Assert.Equal(1, consumer.SuspendedConsumptions);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void SuspendedDrainFreesOnlyTheNewestOfSeveralPublishedSnapshots()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        Publish(ring, sequence: 1);
        SnapshotSlot newest = Publish(ring, sequence: 2);

        Assert.True(consumer.DrainSuspended());

        Assert.Equal(SnapshotSlotState.Free, newest.State);
        Assert.Equal(-1, ring.LatestIndex);
        Assert.Equal(0, consumer.Counters.Consumed);
        Assert.Equal(1, consumer.Counters.Superseded);
        Assert.Equal(1, consumer.SuspendedConsumptions);
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void SuspendedDrainNeverFreesARenderingSlot()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot slot = Publish(ring, sequence: 1);
        SnapshotSlot taken = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, taken);
        Assert.Same(slot, taken);

        Assert.False(consumer.DrainSuspended());
        Assert.Equal(0, consumer.SuspendedConsumptions);
        Assert.Equal(SnapshotSlotState.Rendering, slot.State);
    }

    [Fact]
    public void StopWakesTheRingAndTheConsumerStopsTaking()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        ring.Stop();

        Assert.True(consumer.IsStopped);
        Assert.Null(consumer.TakeForFreeFrame(0));
        Assert.False(consumer.DrainSuspended());
        Assert.Equal(3, ring.Slots.Length);
        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }
    }

    [Fact]
    public void ReleaseAllIsIdempotent()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        SnapshotSlot slot = Publish(ring, sequence: 1);
        SnapshotSlot taken = Take(consumer, frameIndex: 0);
        consumer.AttachToFrame(0, taken);

        consumer.ReleaseAll();
        consumer.ReleaseAll();

        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(0, SnapshotRingAssertions.LiveCount(ring));
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void FramesInFlightOutsideOneOrTwoIsRejected(int framesInFlight)
    {
        using var ring = new SnapshotRing();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SnapshotConsumer(ring, framesInFlight));
    }

    [Fact]
    public void OutOfRangeFrameIndexIsRejected()
    {
        using var ring = new SnapshotRing();
        var consumer = new SnapshotConsumer(ring, framesInFlight: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => consumer.TakeForFreeFrame(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => consumer.TakeForFreeFrame(-1));
    }

    private static SnapshotSlot Take(SnapshotConsumer consumer, int frameIndex)
    {
        SnapshotSlot? slot = consumer.TakeForFreeFrame(frameIndex);
        Assert.NotNull(slot);
        return slot!;
    }

    private static SnapshotSlot Publish(SnapshotRing ring, long sequence)
    {
        SnapshotSlot? slot = ring.Acquire();
        Assert.NotNull(slot);
        slot!.Snapshot.Reset();
        slot.Snapshot.Sequence = sequence;
        ring.Publish(slot);
        return slot;
    }
}
