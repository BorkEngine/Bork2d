namespace Bork2d.Render.Handoff;

/// <summary>
/// The renderer-side consumer of the three-slot ring: it binds the newest <c>Ready</c> snapshot to
/// one GPU frame in flight and releases that slot only when the frame's fence has been observed
/// (Q1=A1, Q7=G1, Q8=H1).
/// </summary>
/// <remarks>
/// <para>
/// The render thread is the only caller. The consumer keeps one slot reference per GPU frame, so a
/// snapshot slot stays <see cref="SnapshotSlotState.Rendering"/> until the frame that submitted it
/// is reused (<see cref="TakeForFreeFrame"/>) or the stop/idle drain releases it
/// (<see cref="ReleaseAll"/>). It never reads scene state; it only moves slot ownership.
/// </para>
/// <para>
/// The suspended path (<see cref="DrainSuspended"/>) frees the newest <c>Ready</c> snapshot without
/// entering <see cref="SnapshotSlotState.Rendering"/> and without a GPU frame; the windowed
/// renderer uses it while minimized or at zero extent. It is not counted as a consumed snapshot.
/// </para>
/// <para>
/// There are no per-frame allocations: the frame array is created once and only slot references
/// move. No lock is taken and no lock is held across a fence wait.
/// </para>
/// </remarks>
internal sealed class SnapshotConsumer
{
    private readonly SnapshotRing _ring;
    private readonly SnapshotSlot?[] _frameSlots;
    private SnapshotSlot? _taken;
    private long _suspendedConsumptions;

    /// <summary>Initializes a new instance of the <see cref="SnapshotConsumer"/> class.</summary>
    /// <param name="ring">The ring the consumer takes snapshots from; the consumer does not own it.</param>
    /// <param name="framesInFlight">The number of GPU frames in flight, <c>1</c> or <c>2</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ring"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="framesInFlight"/> is not between one and the two <c>Rendering</c> slots the
    /// ring allows.
    /// </exception>
    internal SnapshotConsumer(SnapshotRing ring, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (framesInFlight is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesInFlight),
                framesInFlight,
                "The ring holds at most two Rendering slots, so one or two GPU frames are supported.");
        }

        _ring = ring;
        _frameSlots = new SnapshotSlot?[framesInFlight];
    }

    /// <summary>Gets the ring the consumer takes snapshots from.</summary>
    internal SnapshotRing Ring => _ring;

    /// <summary>Gets a value indicating whether a <see cref="SnapshotSlotState.Ready"/> snapshot exists.</summary>
    internal bool HasReadySnapshot => _ring.HasReadySnapshot;

    /// <summary>Gets a value indicating whether the ring was stopped.</summary>
    internal bool IsStopped => _ring.IsStopped;

    /// <summary>Gets the number of snapshots freed without GPU work while suspended (Q8).</summary>
    internal long SuspendedConsumptions => Interlocked.Read(ref _suspendedConsumptions);

    /// <summary>Gets a snapshot of the ring handoff counters.</summary>
    internal SnapshotRingCounters Counters => _ring.Counters;

    /// <summary>
    /// Frees the snapshot slot the frame held when its fence was observed and takes the newest
    /// <c>Ready</c> snapshot for the frame.
    /// </summary>
    /// <param name="frameIndex">The GPU frame index, <c>0..FramesInFlight-1</c>.</param>
    /// <returns>
    /// The taken slot in <see cref="SnapshotSlotState.Rendering"/>, or <see langword="null"/> when
    /// no new snapshot is ready (no GPU frame is recorded).
    /// </returns>
    internal SnapshotSlot? TakeForFreeFrame(int frameIndex)
    {
        ValidateFrame(frameIndex);

        // The caller observed this frame's fence first, so the slot its GPU work referenced is
        // retired before the frame takes another snapshot.
        ReleaseHeld(frameIndex);
        SnapshotSlot? slot = _ring.TakeLatest();
        _taken = slot;
        return slot;
    }

    /// <summary>Attaches the taken snapshot to the GPU frame that was just submitted.</summary>
    /// <param name="frameIndex">The GPU frame index.</param>
    /// <param name="slot">The slot returned by <see cref="TakeForFreeFrame"/>.</param>
    internal void AttachToFrame(int frameIndex, SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ValidateFrame(frameIndex);

        if (ReferenceEquals(_taken, slot))
        {
            _taken = null;
        }

        _frameSlots[frameIndex] = slot;
    }

    /// <summary>
    /// Releases a slot taken for a frame that will not be submitted (for example an out-of-date
    /// swapchain acquire immediately after the take).
    /// </summary>
    /// <param name="slot">The slot returned by <see cref="TakeForFreeFrame"/>.</param>
    internal void ReleaseTaken(SnapshotSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (ReferenceEquals(_taken, slot))
        {
            _taken = null;
        }

        _ring.Release(slot);
    }

    /// <summary>
    /// Releases every snapshot slot the consumer holds: the frame-attached slots and any slot taken
    /// but not yet attached.
    /// </summary>
    /// <remarks>
    /// The caller must first observe every frame fence (or <c>vkDeviceWaitIdle</c>) so no GPU frame
    /// still references the slots. Best-effort: a slot that is no longer
    /// <see cref="SnapshotSlotState.Rendering"/> is skipped instead of throwing during cleanup.
    /// </remarks>
    internal void ReleaseAll()
    {
        for (var index = 0; index < _frameSlots.Length; index++)
        {
            SnapshotSlot? slot = _frameSlots[index];
            _frameSlots[index] = null;
            ReleaseIfRendering(slot);
        }

        SnapshotSlot? taken = _taken;
        _taken = null;
        ReleaseIfRendering(taken);
    }

    /// <summary>
    /// Frees the newest <c>Ready</c> snapshot without GPU work while minimized or at zero extent.
    /// </summary>
    /// <returns><see langword="true"/> when a snapshot was dropped.</returns>
    internal bool DrainSuspended()
    {
        if (!_ring.DiscardLatest())
        {
            return false;
        }

        Interlocked.Increment(ref _suspendedConsumptions);
        return true;
    }

    private void ReleaseHeld(int frameIndex)
    {
        SnapshotSlot? slot = _frameSlots[frameIndex];
        _frameSlots[frameIndex] = null;
        ReleaseIfRendering(slot);
    }

    private void ReleaseIfRendering(SnapshotSlot? slot)
    {
        if (slot is null || slot.State != SnapshotSlotState.Rendering)
        {
            return;
        }

        _ring.Release(slot);
    }

    private void ValidateFrame(int frameIndex)
    {
        if ((uint)frameIndex >= (uint)_frameSlots.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex),
                frameIndex,
                $"The consumer has {_frameSlots.Length} GPU frames in flight.");
        }
    }
}
