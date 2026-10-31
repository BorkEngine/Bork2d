using Bork2d.Render.Handoff;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// The Q20=T1 deterministic 10,000-snapshot stress: a scripted single-threaded interleaving with
/// fake frames/fences, plus a real producer/consumer run whose assertions are relationships and
/// slot invariants (never wall-clock pacing or entity order).
/// </summary>
public sealed class SnapshotRingStressTests
{
    private const int SnapshotCount = 10_000;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void ScriptedTenThousandSnapshotStressKeepsTheInvariants()
    {
        using var ring = new SnapshotRing();
        var frames = new SnapshotSlot?[2];
        long lastConsumed = 0;

        for (var sequence = 1; sequence <= SnapshotCount; sequence++)
        {
            if (sequence % 5 == 0)
            {
                ReleaseOldestFrame(ring, frames);
            }

            if (sequence % 3 == 0)
            {
                TakeNewest(ring, frames, ref lastConsumed);
            }

            if (!SnapshotRingAssertions.HasFreeSlot(ring))
            {
                // The producer would block here; the fake consumer completes a frame instead.
                Assert.Equal(3, SnapshotRingAssertions.LiveCount(ring));
                ReleaseOldestFrame(ring, frames);
            }

            var slot = ring.Acquire();
            Assert.NotNull(slot);
            var snapshot = slot!.Snapshot;
            snapshot.Reset();
            snapshot.Sequence = sequence;
            snapshot.Time = sequence / 60.0;
            snapshot.DeltaTime = 1.0 / 60.0;
            ring.Publish(slot);

            SnapshotRingAssertions.AssertQuiescent(ring);
        }

        while (ring.LatestIndex >= 0)
        {
            TakeNewest(ring, frames, ref lastConsumed);
        }

        ReleaseAllFrames(ring, frames);

        var counters = ring.Counters;
        Assert.Equal(SnapshotCount, lastConsumed);
        Assert.Equal(SnapshotCount, counters.Published);
        Assert.Equal(counters.Published, counters.Consumed + counters.Superseded);
        Assert.True(counters.Superseded > 0, "The script must exercise newest-wins supersession.");
        Assert.Equal(3, counters.MaxLive);
        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, ring.LatestIndex);
        ring.DrainAfterRenderThreadJoined();
        SnapshotRingAssertions.AssertQuiescent(ring);
    }

    [Fact]
    public void ConcurrentTenThousandSnapshotStressHasNoLeakOrLostNewest()
    {
        using var ring = new SnapshotRing();
        using var producerDone = new ManualResetEventSlim();
        using var finalConsumed = new ManualResetEventSlim();
        using var consumerStopped = new ManualResetEventSlim();
        Exception? producerFailure = null;
        Exception? consumerFailure = null;

        var producer = new Thread(() =>
        {
            try
            {
                for (var sequence = 1; sequence <= SnapshotCount; sequence++)
                {
                    var slot = ring.Acquire();
                    if (slot is null)
                    {
                        throw new InvalidOperationException(
                            "The producer was stopped before publishing every snapshot.");
                    }

                    var snapshot = slot.Snapshot;
                    snapshot.Reset();
                    snapshot.Sequence = sequence;
                    snapshot.Time = sequence / 60.0;
                    snapshot.DeltaTime = 1.0 / 60.0;
                    ring.Publish(slot);
                }
            }
            catch (Exception exception)
            {
                producerFailure = exception;
            }
            finally
            {
                producerDone.Set();
            }
        })
        {
            IsBackground = true,
        };

        var consumer = new Thread(() =>
        {
            var frames = new SnapshotSlot?[2];
            try
            {
                while (ring.WaitForSnapshotOrStop())
                {
                    var frameIndex = FirstFreeFrame(frames);
                    if (frameIndex < 0)
                    {
                        ReleaseOldestFrame(ring, frames);
                        frameIndex = FirstFreeFrame(frames);
                    }

                    var slot = ring.TakeLatest();
                    if (slot is null)
                    {
                        continue;
                    }

                    Assert.Equal(SnapshotSlotState.Rendering, slot.State);
                    Assert.InRange(slot.Snapshot.Sequence, 1, SnapshotCount);
                    if (slot.Snapshot.Sequence == SnapshotCount)
                    {
                        finalConsumed.Set();
                    }

                    frames[frameIndex] = slot;
                    SnapshotRingAssertions.AssertConcurrent(ring);
                }
            }
            catch (Exception exception)
            {
                consumerFailure = exception;
            }
            finally
            {
                ReleaseAllFrames(ring, frames);
                consumerStopped.Set();
            }
        })
        {
            IsBackground = true,
        };

        producer.Start();
        consumer.Start();

        Assert.True(producerDone.Wait(_timeout), "The producer did not finish.");
        Assert.True(finalConsumed.Wait(_timeout), "The newest snapshot was not consumed.");

        ring.Stop();

        Assert.True(consumerStopped.Wait(_timeout), "The consumer did not stop.");
        Assert.True(producer.Join(_timeout), "The producer did not join.");
        Assert.True(consumer.Join(_timeout), "The consumer did not join.");

        Assert.Null(producerFailure);
        Assert.Null(consumerFailure);

        var counters = ring.Counters;
        Assert.Equal(SnapshotCount, counters.Published);
        Assert.Equal(counters.Published, counters.Consumed + counters.Superseded);
        Assert.True(counters.Consumed > 0);
        Assert.InRange(counters.MaxLive, 1, 3);
        SnapshotRingAssertions.AssertConcurrent(ring);

        ring.DrainAfterRenderThreadJoined();
        foreach (var slot in ring.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, ring.LatestIndex);
    }

    private static void TakeNewest(SnapshotRing ring, SnapshotSlot?[] frames, ref long lastConsumed)
    {
        var frameIndex = FirstFreeFrame(frames);
        if (frameIndex < 0)
        {
            ReleaseOldestFrame(ring, frames);
            frameIndex = FirstFreeFrame(frames);
        }

        Assert.True(frameIndex >= 0, "A fake frame must be free after a fence.");
        var slot = ring.TakeLatest();
        if (slot is null)
        {
            return;
        }

        Assert.Equal(SnapshotSlotState.Rendering, slot.State);
        Assert.True(
            slot.Snapshot.Sequence > lastConsumed,
            "Consumed snapshots must be strictly newer; the newest snapshot must never be lost.");
        lastConsumed = slot.Snapshot.Sequence;
        frames[frameIndex] = slot;
    }

    private static int FirstFreeFrame(SnapshotSlot?[] frames)
    {
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is null)
            {
                return index;
            }
        }

        return -1;
    }

    private static void ReleaseOldestFrame(SnapshotRing ring, SnapshotSlot?[] frames)
    {
        var index = -1;
        var oldest = long.MaxValue;
        for (var frame = 0; frame < frames.Length; frame++)
        {
            if (frames[frame] is { } slot && slot.Snapshot.Sequence < oldest)
            {
                oldest = slot.Snapshot.Sequence;
                index = frame;
            }
        }

        if (index < 0)
        {
            return;
        }

        var released = frames[index]!;
        Assert.Equal(SnapshotSlotState.Rendering, released.State);
        ring.Release(released);
        frames[index] = null;
    }

    private static void ReleaseAllFrames(SnapshotRing ring, SnapshotSlot?[] frames)
    {
        for (var index = 0; index < frames.Length; index++)
        {
            if (frames[index] is { } slot)
            {
                ring.Release(slot);
                frames[index] = null;
            }
        }
    }
}
