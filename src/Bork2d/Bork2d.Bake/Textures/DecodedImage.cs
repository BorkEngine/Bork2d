namespace Bork2d.Bake.Textures;

/// <summary>
/// A decoder result: straight-alpha RGBA8 pixels, row-major, top-left origin.
/// </summary>
/// <remarks>
/// <see cref="TextureSourceReader"/> validates dimensions and payload length; this type is only a
/// carrier. It does not copy the pixel array.
/// </remarks>
internal sealed class DecodedImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedImage"/> class.
    /// </summary>
    /// <param name="width">The decoded pixel width.</param>
    /// <param name="height">The decoded pixel height.</param>
    /// <param name="pixels">The RGBA8 pixels, <c>width × height × 4</c> bytes.</param>
    internal DecodedImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Gets the decoded pixel width.</summary>
    internal int Width { get; }

    /// <summary>Gets the decoded pixel height.</summary>
    internal int Height { get; }

    /// <summary>Gets the RGBA8 pixels.</summary>
    internal byte[] Pixels { get; }
}
