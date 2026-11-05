namespace Bork2d.Bake.Svg;

/// <summary>
/// The storage format of a baked SVG signed distance field texture.
/// </summary>
internal enum SdfCompression
{
    /// <summary>Uncompressed linear <c>Rgb8</c> data; the default for MSDF output.</summary>
    None = 0,

    /// <summary>BC7 block-compressed <c>CompRgba8</c> data; an explicit authoring choice.</summary>
    Bc7 = 1,
}
