namespace Bork2d.Engine.Rendering;

/// <summary>
/// One completed tick's renderer input, owned by exactly one snapshot slot (M7 WP-2).
/// </summary>
/// <remarks>
/// <para>
/// A frame snapshot is mutable only while its slot is <c>Writing</c>. The tick thread resets and
/// fills it, the slot is published as <c>Ready</c> with a release, and the renderer reads it only
/// while the slot is <c>Rendering</c>; nothing writes it after publication. Cameras and sprites
/// live in compact typed <see cref="RenderDataBuffer{T}"/> pools at the reserved
/// <see cref="CameraSlot"/> and <see cref="SpriteSlot"/>; a registered renderable producer owns a
/// slot at or above <see cref="ReservedSlotCount"/>. Every pool starts empty, grows to the observed
/// high-water mark, and never shrinks (Q6=Z1), so a warm capture appends values without allocating.
/// Resetting a slot clears every buffer's used range, releasing obsolete references without
/// shrinking the buffers. Capture uses no LINQ, boxing, or per-node allocation.
/// </para>
/// <para>
/// The snapshot is asset-agnostic: each sprite carries the immutable <see cref="Assets.SpriteRenderData"/>
/// frozen when its asset was assigned, and no live scene entity or mutable
/// <see cref="Assets.SpriteAsset"/> crosses the boundary.
/// </para>
/// </remarks>
internal sealed class FrameSnapshot
{
    /// <summary>The reserved buffer slot that holds the captured cameras.</summary>
    internal const int CameraSlot = 0;

    /// <summary>The reserved buffer slot that holds the captured sprites.</summary>
    internal const int SpriteSlot = 1;

    /// <summary>
    /// The number of reserved slots. A custom renderable producer's slot is at or above this value,
    /// so it can never collide with the camera or sprite buffers.
    /// </summary>
    internal const int ReservedSlotCount = 2;

    private readonly RenderDataBufferSet _buffers = new();

    internal FrameSnapshot()
    {
        // Create the reserved slots once per slot-owned snapshot, so capacity getters and the first
        // capture never have to grow the set itself.
        _ = _buffers.Get<CameraSnapshot>(CameraSlot);
        _ = _buffers.Get<SpriteSnapshot>(SpriteSlot);
    }

    /// <summary>
    /// Gets or sets the monotonic snapshot sequence; the host starts at <c>1</c> by convention.
    /// </summary>
    internal long Sequence { get; set; }

    /// <summary>Gets or sets the tick's <c>ITime.Time</c> value in seconds.</summary>
    internal double Time { get; set; }

    /// <summary>Gets or sets the tick's <c>ITime.DeltaTime</c> value in seconds.</summary>
    internal double DeltaTime { get; set; }

    /// <summary>Gets the number of captured cameras.</summary>
    internal int CameraCount => _buffers.Get<CameraSnapshot>(CameraSlot).Count;

    /// <summary>Gets the number of captured sprites.</summary>
    internal int SpriteCount => _buffers.Get<SpriteSnapshot>(SpriteSlot).Count;

    /// <summary>
    /// Gets the current camera buffer capacity. Starts at zero, grows to the high-water mark, and
    /// never shrinks (Q6=Z1).
    /// </summary>
    internal int CameraCapacity => _buffers.Get<CameraSnapshot>(CameraSlot).Capacity;

    /// <summary>
    /// Gets the current sprite buffer capacity. Starts at zero, grows to the high-water mark, and
    /// never shrinks (Q6=Z1).
    /// </summary>
    internal int SpriteCapacity => _buffers.Get<SpriteSnapshot>(SpriteSlot).Capacity;

    /// <summary>Gets the captured cameras in capture order (unordered, renderer sorts in M8).</summary>
    internal ReadOnlySpan<CameraSnapshot> Cameras
        => _buffers.Get<CameraSnapshot>(CameraSlot).Items;

    /// <summary>Gets the captured sprites in capture order (unordered, renderer sorts in M8).</summary>
    internal ReadOnlySpan<SpriteSnapshot> Sprites
        => _buffers.Get<SpriteSnapshot>(SpriteSlot).Items;

    /// <summary>
    /// Gets the typed buffer for a renderable producer's slot, creating it on first use.
    /// </summary>
    /// <remarks>
    /// A producer writes its records through this accessor, so no capture code has to know the
    /// concrete renderable or data type. Slots below <see cref="ReservedSlotCount"/> belong to the
    /// engine's cameras and sprites; a custom producer uses a slot at or above it.
    /// </remarks>
    /// <typeparam name="T">The value record type the slot stores.</typeparam>
    /// <param name="slot">The producer's assigned render-data slot.</param>
    /// <returns>The slot's typed buffer.</returns>
    internal RenderDataBuffer<T> GetBuffer<T>(int slot)
        where T : struct
        => _buffers.Get<T>(slot);

    /// <summary>
    /// Clears every buffer's used range, releasing obsolete references, and resets the sequence and
    /// time values.
    /// </summary>
    /// <remarks>
    /// The previously used entries are cleared so no buffer retains render data after a later
    /// capture without that renderable. Capacity and the high-water mark are kept: the buffers
    /// never shrink (Q6=Z1).
    /// </remarks>
    internal void Reset()
    {
        _buffers.Reset();
        Sequence = 0;
        Time = 0;
        DeltaTime = 0;
    }

    /// <summary>
    /// Appends one camera to the reserved camera buffer, growing it when the high-water mark rises.
    /// </summary>
    /// <param name="camera">The camera values to copy.</param>
    internal void AddCamera(in CameraSnapshot camera)
        => _buffers.Get<CameraSnapshot>(CameraSlot).Add(camera);

    /// <summary>
    /// Appends one sprite to the reserved sprite buffer, growing it when the high-water mark rises.
    /// </summary>
    /// <param name="sprite">The sprite values to copy.</param>
    internal void AddSprite(in SpriteSnapshot sprite)
        => _buffers.Get<SpriteSnapshot>(SpriteSlot).Add(sprite);
}
