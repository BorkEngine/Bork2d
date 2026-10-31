namespace Bork2d.Render.Handoff;

/// <summary>
/// The four states of one snapshot slot (D-003).
/// </summary>
/// <remarks>
/// The only legal transitions are <see cref="Free"/> → <see cref="Writing"/> (the tick acquires
/// it), <see cref="Writing"/> → <see cref="Ready"/> (publish) or back to <see cref="Free"/>
/// (abandon), <see cref="Ready"/> → <see cref="Rendering"/> (the renderer takes the newest) or
/// <see cref="Free"/> (a newer publish supersedes it), and <see cref="Rendering"/> →
/// <see cref="Free"/> (the renderer observed the consuming GPU frame's fence).
/// </remarks>
internal enum SnapshotSlotState
{
    /// <summary>The slot is available to the producer.</summary>
    Free,

    /// <summary>The producer owns the slot and is filling its snapshot.</summary>
    Writing,

    /// <summary>The slot is published with a release; it has not been read.</summary>
    Ready,

    /// <summary>The renderer owns the slot until the consuming GPU frame's fence.</summary>
    Rendering,
}
