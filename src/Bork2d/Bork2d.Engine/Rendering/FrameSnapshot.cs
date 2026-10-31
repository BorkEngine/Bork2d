using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// One completed tick's renderer input, owned by exactly one snapshot slot (M7 WP-2).
/// </summary>
/// <remarks>
/// <para>
/// A frame snapshot is mutable only while its slot is <c>Writing</c>. The tick thread resets and
/// fills it, the slot is published as <c>Ready</c> with a release, and the renderer reads it only
/// while the slot is <c>Rendering</c>; nothing writes it after publication. The camera and sprite
/// arrays are slot-private pooling buffers: they start empty, grow to the observed high-water
/// mark, and never shrink (Q6=Z1), so a warm capture appends values without allocating. Capture
/// uses no LINQ, boxing, or per-node allocation.
/// </para>
/// <para>
/// The snapshot is asset-agnostic: the loaded <see cref="SpriteAsset"/> reference is the only
/// object reference and no live scene entity crosses the boundary.
/// </para>
/// </remarks>
internal sealed class FrameSnapshot
{
    private CameraSnapshot[] _cameras = Array.Empty<CameraSnapshot>();
    private SpriteSnapshot[] _sprites = Array.Empty<SpriteSnapshot>();
    private int _cameraCount;
    private int _spriteCount;

    /// <summary>
    /// Gets or sets the monotonic snapshot sequence; the host starts at <c>1</c> by convention.
    /// </summary>
    internal long Sequence { get; set; }

    /// <summary>Gets or sets the tick's <c>ITime.Time</c> value in seconds.</summary>
    internal double Time { get; set; }

    /// <summary>Gets or sets the tick's <c>ITime.DeltaTime</c> value in seconds.</summary>
    internal double DeltaTime { get; set; }

    /// <summary>Gets the number of captured cameras.</summary>
    internal int CameraCount => _cameraCount;

    /// <summary>Gets the number of captured sprites.</summary>
    internal int SpriteCount => _spriteCount;

    /// <summary>
    /// Gets the current camera buffer capacity. Starts at zero, grows to the high-water mark, and
    /// never shrinks (Q6=Z1).
    /// </summary>
    internal int CameraCapacity => _cameras.Length;

    /// <summary>
    /// Gets the current sprite buffer capacity. Starts at zero, grows to the high-water mark, and
    /// never shrinks (Q6=Z1).
    /// </summary>
    internal int SpriteCapacity => _sprites.Length;

    /// <summary>Gets the captured cameras in capture order (unordered, renderer sorts in M8).</summary>
    internal ReadOnlySpan<CameraSnapshot> Cameras => _cameras.AsSpan(0, _cameraCount);

    /// <summary>Gets the captured sprites in capture order (unordered, renderer sorts in M8).</summary>
    internal ReadOnlySpan<SpriteSnapshot> Sprites => _sprites.AsSpan(0, _spriteCount);

    /// <summary>
    /// Clears the captured counts and the sequence/time values while keeping the pooled buffers.
    /// </summary>
    internal void Reset()
    {
        _cameraCount = 0;
        _spriteCount = 0;
        Sequence = 0;
        Time = 0;
        DeltaTime = 0;
    }

    /// <summary>
    /// Appends one camera, growing the slot-private buffer when the high-water mark rises.
    /// </summary>
    /// <param name="camera">The camera values to copy.</param>
    internal void AddCamera(in CameraSnapshot camera)
    {
        if (_cameraCount == _cameras.Length)
        {
            Array.Resize(ref _cameras, Grow(_cameras.Length));
        }

        _cameras[_cameraCount++] = camera;
    }

    /// <summary>
    /// Appends one sprite, growing the slot-private buffer when the high-water mark rises.
    /// </summary>
    /// <param name="sprite">The sprite values to copy.</param>
    internal void AddSprite(in SpriteSnapshot sprite)
    {
        if (_spriteCount == _sprites.Length)
        {
            Array.Resize(ref _sprites, Grow(_sprites.Length));
        }

        _sprites[_spriteCount++] = sprite;
    }

    private static int Grow(int capacity) => capacity == 0 ? 4 : capacity * 2;
}
