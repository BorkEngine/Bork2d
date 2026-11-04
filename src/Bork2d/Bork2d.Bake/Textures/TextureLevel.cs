namespace Bork2d.Bake.Textures;

/// <summary>
/// One straight-alpha RGBA8 level of a generated mip chain.
/// </summary>
/// <remarks>
/// The bake generates every level once as RGBA8 and then packs or block-encodes each level for the
/// selected format. The pixel array is owned by the level and is never shared with a caller.
/// </remarks>
internal sealed class TextureLevel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureLevel"/> class.
    /// </summary>
    /// <param name="width">The level's pixel width.</param>
    /// <param name="height">The level's pixel height.</param>
    /// <param name="pixels">The straight-alpha RGBA8 pixels, <c>width × height × 4</c> bytes.</param>
    internal TextureLevel(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Gets the level's pixel width.</summary>
    internal int Width { get; }

    /// <summary>Gets the level's pixel height.</summary>
    internal int Height { get; }

    /// <summary>Gets the level's straight-alpha RGBA8 pixels.</summary>
    internal byte[] Pixels { get; }
}
