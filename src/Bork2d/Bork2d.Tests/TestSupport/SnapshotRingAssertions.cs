using Bork2d.Render.Handoff;
using Xunit;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Shared invariant assertions for the three-slot snapshot ring (D-003, M7 WP-2).
/// </summary>
/// <remarks>
/// The quiescent form is used by the single-threaded state-machine tests, where no publish can be
/// in flight; the concurrent form is used by the producer/consumer stress, where a publish may be
/// transiently mid-flight (the new slot is <c>Ready</c> just before <c>latest</c> is exchanged).
/// </remarks>
internal static class SnapshotRingAssertions
{
    /// <summary>Asserts every stable slot invariant at a quiescent point.</summary>
    /// <param name="ring">The ring to inspect.</param>
    internal static void AssertQuiescent(SnapshotRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var writing = 0;
        var ready = 0;
        var rendering = 0;
        foreach (var slot in ring.Slots)
        {
            switch (slot.State)
            {
                case SnapshotSlotState.Writing:
                    writing++;
                    break;
                case SnapshotSlotState.Ready:
                    ready++;
                    break;
                case SnapshotSlotState.Rendering:
                    rendering++;
                    break;
                default:
                    break;
            }
        }

        Assert.True(writing <= 1, $"At most one slot may be Writing; found {writing}.");
        Assert.True(ready <= 1, $"At most one slot may be Ready; found {ready}.");
        Assert.True(
            ready + rendering <= SnapshotRing.SlotCapacity,
            $"Ready + Rendering must stay within the ring; found {ready + rendering}.");
        Assert.True(rendering <= 2, $"At most two slots may be Rendering; found {rendering}.");
        AssertLatest(ring, allowConcurrentPublish: false);
    }

    /// <summary>Asserts the invariants that hold while a producer and consumer run concurrently.</summary>
    /// <param name="ring">The ring to inspect.</param>
    internal static void AssertConcurrent(SnapshotRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var writing = 0;
        var rendering = 0;
        foreach (var slot in ring.Slots)
        {
            switch (slot.State)
            {
                case SnapshotSlotState.Writing:
                    writing++;
                    break;
                case SnapshotSlotState.Rendering:
                    rendering++;
                    break;
                default:
                    break;
            }
        }

        Assert.True(writing <= 1, $"At most one slot may be Writing; found {writing}.");
        Assert.True(rendering <= 2, $"At most two slots may be Rendering; found {rendering}.");
        AssertLatest(ring, allowConcurrentPublish: true);
    }

    /// <summary>Counts the <c>Ready + Rendering</c> slots (the live snapshot count).</summary>
    /// <param name="ring">The ring to inspect.</param>
    /// <returns>The live snapshot count, <c>0..3</c>.</returns>
    internal static int LiveCount(SnapshotRing ring)
    {
        var live = 0;
        foreach (var slot in ring.Slots)
        {
            if (slot.State is SnapshotSlotState.Ready or SnapshotSlotState.Rendering)
            {
                live++;
            }
        }

        return live;
    }

    /// <summary>Tests whether any slot is <see cref="SnapshotSlotState.Free"/>.</summary>
    /// <param name="ring">The ring to inspect.</param>
    /// <returns><see langword="true"/> when at least one slot is free.</returns>
    internal static bool HasFreeSlot(SnapshotRing ring)
    {
        foreach (var slot in ring.Slots)
        {
            if (slot.State == SnapshotSlotState.Free)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertLatest(SnapshotRing ring, bool allowConcurrentPublish)
    {
        var latest = ring.LatestIndex;
        Assert.InRange(latest, -1, SnapshotRing.SlotCapacity - 1);

        // A publish marks the new slot Ready before exchanging latest and only then frees the
        // previous Ready slot, so a concurrent reader can observe a stale latest whose slot was
        // just freed. Retry while latest keeps moving; a stable invalid latest is a real bug.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (latest < 0)
            {
                return;
            }

            var state = ring.Slots[latest].State;
            if (state is SnapshotSlotState.Ready or SnapshotSlotState.Rendering)
            {
                return;
            }

            if (!allowConcurrentPublish)
            {
                Assert.Fail($"latest must never point at a {state} slot.");
            }

            var observed = ring.LatestIndex;
            if (observed == latest)
            {
                Assert.Fail($"latest must never point at a {state} slot.");
            }

            latest = observed;
        }
    }
}
