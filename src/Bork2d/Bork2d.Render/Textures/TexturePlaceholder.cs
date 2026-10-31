using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// The generated 8×8 magenta/black checkerboard placeholder page (M8 §6.5).
/// </summary>
/// <remarks>
/// The placeholder is index <c>0</c>, always resident, and is created and uploaded at renderer
/// startup. It is RGBA8, nearest-filter, clamp-wrap, one mip, with no payload asset: the pixels are
/// generated from CPU bytes and uploaded through the same page path as real content. Its creation
/// or upload failure is a renderer startup failure.
/// </remarks>
internal static class TexturePlaceholder
{
    /// <summary>The placeholder's width and height in pixels.</summary>
    internal const int Size = 8;

    /// <summary>The placeholder's payload length in bytes (<c>8 * 8 * 4</c>).</summary>
    internal const int PayloadBytes = Size * Size * 4;

    private const byte MagentaRed = 255;
    private const byte MagentaGreen = 0;
    private const byte MagentaBlue = 255;
    private const byte BlackLevel = 0;
    private const byte Opaque = 255;

    /// <summary>
    /// Creates the 8×8 checkerboard RGBA8 pixels, row-major, top-left origin.
    /// </summary>
    /// <remarks>
    /// Cell <c>(x, y)</c> is magenta when <c>(x + y)</c> is even and opaque black otherwise, so the
    /// pattern starts magenta at the top-left and alternates every texel. Alpha is always <c>255</c>.
    /// </remarks>
    /// <returns>A new 256-byte payload; the caller owns it.</returns>
    internal static byte[] CreatePixels()
    {
        var pixels = new byte[PayloadBytes];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                bool magenta = ((x + y) & 1) == 0;
                int offset = ((y * Size) + x) * 4;
                pixels[offset] = magenta ? MagentaRed : BlackLevel;
                pixels[offset + 1] = magenta ? MagentaGreen : BlackLevel;
                pixels[offset + 2] = magenta ? MagentaBlue : BlackLevel;
                pixels[offset + 3] = Opaque;
            }
        }

        return pixels;
    }

    /// <summary>
    /// Creates the placeholder's sampler/format description: RGBA8, nearest filter, clamp wrap.
    /// </summary>
    /// <returns>A descriptor with no payload reference.</returns>
    internal static TextureDescriptor CreateDescriptor() => new()
    {
        Width = Size,
        Height = Size,
        Format = TextureFormat.Rgba8,
        Filter = TextureFilter.Nearest,
        Wrap = TextureWrap.Clamp,
        Payload = DataRef.None,
    };
}
