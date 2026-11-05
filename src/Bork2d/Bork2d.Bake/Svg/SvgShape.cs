namespace Bork2d.Bake.Svg;

/// <summary>
/// One fillable shape: the painted geometry of one SVG shape element.
/// </summary>
/// <remarks>
/// <para>
/// The contours are already cleaned (no zero-length segments and no degenerate contours) and
/// normalized to the nonzero representation: an even depth contour has a positive signed area and an
/// odd depth contour has a negative one. Subtask 2 can therefore feed them to a nonzero-fill MSDF
/// generator without further orientation work.
/// </para>
/// <para>
/// Only holes formed by nested subpaths are guaranteed. Self-intersecting or overlapping subpaths
/// keep the orientation result of the normalization and are not an error, so a bake should treat
/// them as authored content rather than reject them.
/// </para>
/// </remarks>
internal sealed class SvgShape
{
    private readonly SvgContour[] _contours;

    /// <summary>Initializes a new instance of the <see cref="SvgShape"/> class.</summary>
    /// <param name="fillRule">The authored fill rule before normalization.</param>
    /// <param name="contours">The normalized, closed contours; at least one.</param>
    /// <exception cref="ArgumentException"><paramref name="contours"/> is empty.</exception>
    internal SvgShape(SvgFillRule fillRule, IReadOnlyList<SvgContour> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);
        if (contours.Count == 0)
        {
            throw new ArgumentException("A shape requires at least one contour.", nameof(contours));
        }

        FillRule = fillRule;
        _contours = [.. contours];
    }

    /// <summary>Gets the fill rule the shape was authored with.</summary>
    internal SvgFillRule FillRule { get; }

    /// <summary>Gets the normalized contours in document order.</summary>
    internal IReadOnlyList<SvgContour> Contours => _contours;
}
