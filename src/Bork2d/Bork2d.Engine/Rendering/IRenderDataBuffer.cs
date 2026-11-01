namespace Bork2d.Engine.Rendering;

/// <summary>
/// The non-generic view of one typed render-data buffer owned by a <see cref="RenderDataBufferSet"/>.
/// </summary>
/// <remarks>
/// Slot buffers must be usable without knowing their element type, so the owning set can reset
/// every buffer's used range in one allocation-free pass. Typed access goes through
/// <see cref="RenderDataBuffer{T}"/>; the <see cref="ItemType"/> check lets the set reject reusing
/// an occupied slot for a different data type.
/// </remarks>
internal interface IRenderDataBuffer
{
    /// <summary>
    /// Gets the element type this buffer stores.
    /// </summary>
    Type ItemType { get; }

    /// <summary>
    /// Gets the number of appended items.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the buffer capacity, which grows to the high-water mark and never shrinks.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Clears the used range so it retains no values, keeping the capacity high-water mark.
    /// </summary>
    void Reset();
}
