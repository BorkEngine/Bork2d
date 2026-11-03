namespace Bork2d.Render.Sprites;

/// <summary>
/// The per-frame global uniform sink the sprite frame planner writes into.
/// </summary>
/// <remarks>
/// The production implementation is the frame's mapped host-visible dynamic uniform buffer
/// (<c>FrameInstanceResources</c>); deterministic tests substitute an in-memory fake. The block
/// stride is the device's <c>minUniformBufferOffsetAlignment</c>-aligned block size, so a dynamic
/// offset can select any written block. The renderer grows the buffer only after the frame fence
/// has signaled, following the same rule as the instance buffer.
/// </remarks>
internal interface IGlobalUniformBuffer
{
    /// <summary>Gets the current block capacity.</summary>
    int Capacity { get; }

    /// <summary>Gets the device-aligned byte stride between consecutive blocks.</summary>
    int BlockStride { get; }

    /// <summary>
    /// Ensures the buffer holds at least <paramref name="requiredBlocks"/> global blocks.
    /// </summary>
    /// <remarks>
    /// The renderer calls this only after the frame's fence was observed, so no GPU work references
    /// the buffer and a reallocation is safe. A required block count that cannot be represented is a
    /// fatal <see cref="Bork2d.Engine.Bork2dException"/>; blocks are never truncated.
    /// </remarks>
    /// <param name="requiredBlocks">The frame's required block count.</param>
    void EnsureCapacity(int requiredBlocks);

    /// <summary>Writes one global block at <paramref name="index"/>.</summary>
    /// <param name="index">The block index inside the current capacity.</param>
    /// <param name="block">The block to write.</param>
    void WriteBlock(int index, in GlobalUniformBlock block);
}
