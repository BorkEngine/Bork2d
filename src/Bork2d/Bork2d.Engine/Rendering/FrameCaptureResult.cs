using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// The immutable result of one <c>IEngine.CaptureFrameAsync</c> request (M8 §6.9).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Pixels"/> is row-major RGBA8 with a top-left origin and is exactly
/// <c>Width * Height * 4</c> bytes. The dimensions are the serving frame's actual framebuffer
/// extent, so a request that spans a resize yields the extent of the frame that recorded the copy,
/// not the extent observed when the request was made. <see cref="Format"/> is always
/// <see cref="TextureFormat.Rgba8"/>.
/// </para>
/// <para>
/// The bytes are a shared immutable capture result: every request served by the same GPU frame
/// observes the same memory, and callers must not mutate it. One array is allocated per captured
/// frame, not per request.
/// </para>
/// </remarks>
/// <param name="Pixels">The captured frame's row-major, top-left-origin RGBA8 bytes.</param>
/// <param name="Width">The captured frame's width in pixels.</param>
/// <param name="Height">The captured frame's height in pixels.</param>
/// <param name="Format">The pixel format, always <see cref="TextureFormat.Rgba8"/>.</param>
public readonly record struct FrameCaptureResult(
    ReadOnlyMemory<byte> Pixels,
    int Width,
    int Height,
    TextureFormat Format);
