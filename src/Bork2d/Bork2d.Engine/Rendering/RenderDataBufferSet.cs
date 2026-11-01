namespace Bork2d.Engine.Rendering;

/// <summary>
/// Owns the typed render-data buffers of one <see cref="FrameSnapshot"/> by contiguous slot.
/// </summary>
/// <remarks>
/// <para>
/// Slot <c>0</c> and slot <c>1</c> are reserved for cameras and sprites; every other slot belongs
/// to a registered renderable producer. A slot's buffer is created on first access and then kept
/// for the lifetime of the snapshot, so a warm capture only appends values.
/// </para>
/// <para>
/// A slot is typed by its first user. Asking for an occupied slot with a different element type is
/// a programming error and is rejected instead of silently reinterpreting the storage.
/// </para>
/// </remarks>
internal sealed class RenderDataBufferSet
{
    private IRenderDataBuffer?[] _buffers = Array.Empty<IRenderDataBuffer?>();

    /// <summary>
    /// Gets the typed buffer for <paramref name="slot"/>, creating it on first use.
    /// </summary>
    /// <typeparam name="T">The value record type the slot stores.</typeparam>
    /// <param name="slot">
    /// The zero-based render-data slot. Slots are created in order, so a new slot must be the next
    /// contiguous index.
    /// </param>
    /// <returns>The slot's typed buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is negative.</exception>
    /// <exception cref="Bork2dException">
    /// The slot is not contiguous, or it already holds a buffer of a different element type.
    /// </exception>
    internal RenderDataBuffer<T> Get<T>(int slot)
        where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);

        if (slot > _buffers.Length)
        {
            throw new Bork2dException(
                $"Render-data slot {slot} cannot be created before the contiguous slot "
                    + $"{_buffers.Length}.");
        }

        if (slot == _buffers.Length)
        {
            Array.Resize(ref _buffers, slot + 1);
        }

        if (_buffers[slot] is not { } buffer)
        {
            var created = new RenderDataBuffer<T>();
            _buffers[slot] = created;
            return created;
        }

        if (buffer is RenderDataBuffer<T> typed)
        {
            return typed;
        }

        throw new Bork2dException(
            $"Render-data slot {slot} already holds '{buffer.ItemType.FullName}' records; it cannot "
                + $"also hold '{typeof(T).FullName}' records.");
    }

    /// <summary>
    /// Clears every existing buffer's used range, keeping every capacity high-water mark.
    /// </summary>
    internal void Reset()
    {
        for (var index = 0; index < _buffers.Length; index++)
        {
            _buffers[index]?.Reset();
        }
    }
}
