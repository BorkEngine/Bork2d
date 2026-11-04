namespace Bork2d.Engine.Assets;

/// <summary>
/// Selects the pixel format of a baked KTX2 texture payload.
/// </summary>
/// <remarks>
/// <para>
/// Every format is interpreted as linear data: there is no sRGB texture format and no color-space
/// conversion in the bake or the runtime. <see cref="Rgba8"/>, <see cref="Rgb8"/>, <see cref="Rg8"/>,
/// and <see cref="R8"/> are uncompressed UNORM formats; <see cref="CompRgba8"/>,
/// <see cref="CompRgb8"/>, <see cref="CompRg8"/>, and <see cref="CompR8"/> are Vulkan
/// block-compressed formats (BC7, BC1, BC5, and BC4 respectively).
/// </para>
/// <para>
/// <see cref="Rgb8"/> is logical RGB stored as RGBA8 UNORM with alpha forced to <c>255</c>.
/// <see cref="CompRgb8"/> is opaque BC1: alpha is not part of the asset and decodes as <c>1</c>.
/// The value is stored as an integer in the pack, so the assignment below is a stable wire contract.
/// </para>
/// </remarks>
public enum TextureFormat
{
    /// <summary>RGBA8 UNORM: four 8-bit channels, four bytes per pixel.</summary>
    Rgba8 = 0,

    /// <summary>Logical RGB stored as RGBA8 UNORM with alpha forced to <c>255</c>.</summary>
    Rgb8 = 1,

    /// <summary>RG8 UNORM: red and green, two bytes per pixel.</summary>
    Rg8 = 2,

    /// <summary>R8 UNORM: one 8-bit red channel per pixel.</summary>
    R8 = 3,

    /// <summary>BC7 UNORM (four channels).</summary>
    CompRgba8 = 4,

    /// <summary>Opaque BC1 (RGB only; decoded alpha is <c>1</c>).</summary>
    CompRgb8 = 5,

    /// <summary>BC5 UNORM (two channels).</summary>
    CompRg8 = 6,

    /// <summary>BC4 UNORM (one channel).</summary>
    CompR8 = 7,
}
