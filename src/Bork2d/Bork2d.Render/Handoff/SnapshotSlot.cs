using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Handoff;

/// <summary>
/// One of the three snapshot slots: a state and a slot-private <see cref="FrameSnapshot"/>
/// (Q1=A1, Q6=Z1).
/// </summary>
/// <remarks>
/// <para>
/// State transitions are lock-free compare-exchanges; the release on a publish is the
/// <see cref="SnapshotSlotState.Ready"/> transition and the consumer's take is its acquire. The
/// slot only exposes the transition primitive to the owning <see cref="SnapshotRing"/> and tests;
/// no producer or consumer may set a state directly.
/// </para>
/// <para>
/// The snapshot buffer belongs to this slot for the application's lifetime. It starts empty and
/// grows to the high-water mark on first capture; it never shrinks.
/// </para>
/// </remarks>
internal sealed class SnapshotSlot
{
    private readonly FrameSnapshot _snapshot;
    private int _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotSlot"/> class in the
    /// <see cref="SnapshotSlotState.Free"/> state.
    /// </summary>
    /// <param name="index">The slot index within the ring, <c>0..2</c>.</param>
    /// <param name="snapshot">The slot-private snapshot buffer.</param>
    internal SnapshotSlot(int index, FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Index = index;
        _snapshot = snapshot;
        _state = (int)SnapshotSlotState.Free;
    }

    /// <summary>Gets the slot index within the ring, <c>0..2</c>.</summary>
    internal int Index { get; }

    /// <summary>Gets the slot-private snapshot buffer.</summary>
    internal FrameSnapshot Snapshot => _snapshot;

    /// <summary>Gets the current state with an acquire read.</summary>
    internal SnapshotSlotState State => (SnapshotSlotState)Volatile.Read(ref _state);

    /// <summary>
    /// Atomically moves the slot from <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The expected current state.</param>
    /// <param name="to">The target state.</param>
    /// <returns><see langword="true"/> when the transition won; <see langword="false"/> otherwise.</returns>
    internal bool TryTransition(SnapshotSlotState from, SnapshotSlotState to)
    {
        return Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;
    }
}
