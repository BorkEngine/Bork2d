namespace Bork2d.Render.Sprites;

/// <summary>
/// The per-frame instance sink the sprite frame planner writes into (M8 §6.6).
/// </summary>
/// <remarks>
/// The production implementation is the frame's mapped host-visible storage buffer
/// (<c>FrameInstanceResources</c>); deterministic tests substitute an in-memory fake. The planner
/// grows the buffer once per frame, after the frame fence has signaled, and then writes every
/// record directly into the mapping; there is no CPU frame array and no staging copy.
/// </remarks>
internal interface IInstanceBuffer
{
    /// <summary>Gets the current instance-record capacity.</summary>
    int InstanceCapacity { get; }

    /// <summary>
    /// Ensures the buffer holds at least <paramref name="requiredInstances"/> 96-byte records.
    /// </summary>
    /// <remarks>
    /// The renderer calls this only after the frame's fence was observed, so no GPU work references
    /// the buffer and a reallocation is safe. A required count above the device's
    /// <c>maxStorageBufferRange</c> is a fatal <see cref="Bork2d.Engine.Bork2dException"/>; records
    /// are never truncated.
    /// </remarks>
    /// <param name="requiredInstances">The frame's total instance count.</param>
    void EnsureCapacity(int requiredInstances);

    /// <summary>Writes one instance record at <paramref name="index"/>.</summary>
    /// <param name="index">The record index inside the current capacity.</param>
    /// <param name="instance">The record to write.</param>
    void WriteInstance(int index, in SpriteInstance instance);
}
