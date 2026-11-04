namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The per-frame line vertex/index buffer sink the 2D planner writes drawable line geometry through.
/// </summary>
/// <remarks>
/// <para>
/// The production implementation is <c>FrameInstanceResources</c>, which owns one host-visible
/// coherent, mapped vertex buffer and one index buffer per GPU frame. Deterministic tests substitute
/// an in-memory fake so the planned vertex/index data, world transforms, absolute indices, and run
/// ranges can be asserted without Vulkan.
/// </para>
/// <para>
/// <see cref="EnsureCapacity"/> must be called once with the frame's complete totals before any
/// write. The frame fence must already have signaled: capacity growth recreates the buffers, so a
/// sink that is still in flight must not be grown. All members run on the render thread.
/// </para>
/// </remarks>
internal interface ILineBufferSink
{
    /// <summary>Gets the current vertex-record capacity.</summary>
    int VertexCapacity { get; }

    /// <summary>Gets the current 32-bit index capacity.</summary>
    int IndexCapacity { get; }

    /// <summary>
    /// Ensures the sink holds at least the frame's total vertex and index counts, growing the
    /// buffers once when needed.
    /// </summary>
    /// <param name="vertexCount">The frame's total line vertex count.</param>
    /// <param name="indexCount">The frame's total line index count.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    /// <exception cref="Bork2d.Engine.Bork2dException">
    /// A buffer cannot hold the frame without exceeding a device limit; never truncates.
    /// </exception>
    void EnsureCapacity(int vertexCount, int indexCount);

    /// <summary>Writes one vertex record at a frame-buffer index.</summary>
    /// <param name="index">The absolute vertex index.</param>
    /// <param name="vertex">The record to write.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the current capacity.</exception>
    void WriteVertex(int index, in LineVertex vertex);

    /// <summary>Writes one 32-bit index at a frame-buffer index.</summary>
    /// <param name="index">The absolute index-buffer position.</param>
    /// <param name="value">The absolute vertex index.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the current capacity.</exception>
    void WriteIndex(int index, uint value);
}
