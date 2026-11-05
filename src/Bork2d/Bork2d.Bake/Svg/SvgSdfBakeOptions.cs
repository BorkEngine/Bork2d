namespace Bork2d.Bake.Svg;

/// <summary>
/// The authored options of one SVG signed distance field bake.
/// </summary>
/// <remarks>
/// The baker validates the values and reports <c>BK9204</c> instead of throwing, because the values
/// come from a sidecar. <see cref="Resolution"/> is the long-axis pixel budget, <see cref="Range"/>
/// is the distance spread stored in the field, and <see cref="Padding"/> is extra margin beyond the
/// range.
/// </remarks>
/// <param name="Resolution">The long-axis pixel budget, at most <see cref="MaxResolution"/>.</param>
/// <param name="Range">The distance spread in pixels; greater than zero.</param>
/// <param name="Padding">The extra margin in pixels beyond the range; at least zero.</param>
/// <param name="Compression">The storage format.</param>
internal readonly record struct SvgSdfBakeOptions(
    int Resolution,
    int Range,
    int Padding,
    SdfCompression Compression)
{
    /// <summary>The default long-axis pixel budget.</summary>
    internal const int DefaultResolution = 512;

    /// <summary>The default distance spread in pixels.</summary>
    internal const int DefaultRange = 8;

    /// <summary>The default extra margin in pixels beyond the range.</summary>
    internal const int DefaultPadding = 2;

    /// <summary>The largest permitted long-axis pixel budget.</summary>
    internal const int MaxResolution = 8192;
}
