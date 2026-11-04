namespace Bork2d.Bake;

/// <summary>
/// The semantic channel type selected by a texture sidecar's <c>type</c> key.
/// </summary>
/// <remarks>
/// The channel type is authoring metadata, not a payload format: the bake maps it together with
/// <see cref="TextureCompression"/> to a concrete <c>TextureFormat</c>. Every type is linear; there is
/// no sRGB variant. <see cref="Rgb"/> alpha is not part of the asset and is written as <c>255</c>.
/// </remarks>
public enum TextureChannelType
{
    /// <summary>All four channels: red, green, blue, and alpha.</summary>
    Rgba = 0,

    /// <summary>Red, green, and blue; alpha is forced to opaque.</summary>
    Rgb = 1,

    /// <summary>Red and green only.</summary>
    Rg = 2,

    /// <summary>Red only.</summary>
    R = 3,
}
