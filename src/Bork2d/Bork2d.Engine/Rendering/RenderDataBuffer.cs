namespace Bork2d.Engine.Rendering;

/// <summary>
/// A typed, slot-private pool of value records for one renderable kind in a frame snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The buffer starts empty, grows to the observed high-water mark, and never shrinks, so a warm
/// capture appends values without allocating. <see cref="Reset"/> clears the previously used range,
/// releasing any references the records hold, and keeps the capacity.
/// </para>
/// <para>
/// Items are structs: capture copies value-only snapshot records, never live scene entities.
/// </para>
/// </remarks>
/// <typeparam name="T">The value record type the buffer stores.</typeparam>
internal sealed class RenderDataBuffer<T> : IRenderDataBuffer
    where T : struct
{
    private T[] _items = Array.Empty<T>();
    private int _count;

    /// <summary>Gets the number of appended items.</summary>
    internal int Count => _count;

    /// <summary>
    /// Gets the buffer capacity. Starts at zero, grows to the high-water mark, and never shrinks.
    /// </summary>
    internal int Capacity => _items.Length;

    /// <summary>Gets the appended items in capture order.</summary>
    internal ReadOnlySpan<T> Items => _items.AsSpan(0, _count);

    /// <inheritdoc/>
    Type IRenderDataBuffer.ItemType => typeof(T);

    /// <inheritdoc/>
    int IRenderDataBuffer.Count => _count;

    /// <inheritdoc/>
    int IRenderDataBuffer.Capacity => _items.Length;

    /// <summary>
    /// Appends one record, growing the slot-private buffer when the high-water mark rises.
    /// </summary>
    /// <param name="item">The value record to copy.</param>
    internal void Add(in T item)
    {
        if (_count == _items.Length)
        {
            Array.Resize(ref _items, Grow(_items.Length));
        }

        _items[_count++] = item;
    }

    /// <summary>
    /// Clears the used range, releasing any references it retains, and keeps the capacity.
    /// </summary>
    internal void Reset()
    {
        _items.AsSpan(0, _count).Clear();
        _count = 0;
    }

    /// <inheritdoc/>
    void IRenderDataBuffer.Reset() => Reset();

    private static int Grow(int capacity) => capacity == 0 ? 4 : capacity * 2;
}
