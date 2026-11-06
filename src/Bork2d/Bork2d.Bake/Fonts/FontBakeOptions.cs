namespace Bork2d.Bake.Fonts;

/// <summary>
/// The authored options of one font MSDF atlas bake.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Resolution"/> is the fixed side length of the square atlas in pixels. It is the
/// sidecar's maximum dimension and also the texture size: the bake never crops the atlas, so a small
/// selection fills more of it and a large selection receives a smaller derived em size.
/// </para>
/// <para>
/// <see cref="Range"/> is the MSDF distance spread in atlas pixels and <see cref="Padding"/> is extra
/// pixels beyond the range. Each glyph cell reserves <c>range + padding</c> pixels on every side, so
/// the field never touches a cell boundary.
/// </para>
/// </remarks>
/// <param name="Resolution">The fixed square atlas side length in pixels.</param>
/// <param name="Range">The distance spread in pixels; greater than zero.</param>
/// <param name="Padding">The extra margin in pixels beyond the range; at least zero.</param>
internal readonly record struct FontBakeOptions(
    int Resolution,
    int Range,
    int Padding)
{
    /// <summary>The default fixed atlas side length.</summary>
    internal const int DefaultResolution = 2048;

    /// <summary>The default distance spread in pixels.</summary>
    internal const int DefaultRange = 8;

    /// <summary>The default extra margin in pixels beyond the range.</summary>
    internal const int DefaultPadding = 2;

    /// <summary>The largest permitted atlas side length.</summary>
    internal const int MaxResolution = 8192;

    /// <summary>The smallest em size the baker tries before reporting that nothing fits.</summary>
    internal const int MinPixelsPerEm = 4;

    /// <summary>The default selection: printable ASCII, space through tilde.</summary>
    internal const string DefaultRangeSelection = "U+0020-U+007E";
}
