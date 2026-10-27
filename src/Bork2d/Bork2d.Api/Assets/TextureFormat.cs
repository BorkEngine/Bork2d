namespace Bork2d.Api.Assets;

/// <summary>
/// Selects the pixel format of a baked texture payload.
/// </summary>
/// <remarks>
/// <see cref="Rgba8"/> is the only v1 format and the default; KTX2 is later. The value is stored as
/// an integer in the pack.
/// </remarks>
public enum TextureFormat
{
    /// <summary>Straight-alpha 8-bit RGBA, four bytes per pixel.</summary>
    Rgba8 = 0,
}
