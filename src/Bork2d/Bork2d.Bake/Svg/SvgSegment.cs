namespace Bork2d.Bake.Svg;

/// <summary>
/// One segment of a closed <see cref="SvgContour"/>.
/// </summary>
/// <remarks>
/// A segment is immutable and knows only its own start and end. The contour guarantees that each
/// segment starts where the previous one ended and that the last one ends at the first one's start,
/// so a segment never carries duplicate closing geometry. <see cref="PointAt"/> takes a parameter in
/// <c>[0,1]</c>; values outside that interval are extrapolated rather than clamped.
/// </remarks>
internal abstract class SvgSegment
{
    /// <summary>The maximum number of flattened pieces one curved segment may expand to.</summary>
    protected const int MaximumFlatteningSteps = 4096;

    /// <summary>Initializes a new instance of the <see cref="SvgSegment"/> class.</summary>
    /// <param name="start">The segment start point.</param>
    /// <param name="end">The segment end point.</param>
    private protected SvgSegment(SvgPoint start, SvgPoint end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Gets the segment start point.</summary>
    internal SvgPoint Start { get; }

    /// <summary>Gets the segment end point.</summary>
    internal SvgPoint End { get; }

    /// <summary>Evaluates the segment at the given parameter.</summary>
    /// <param name="t">The parameter; the segment runs from <c>0</c> (start) to <c>1</c> (end).</param>
    /// <returns>The point on the segment.</returns>
    internal abstract SvgPoint PointAt(double t);

    /// <summary>Creates the reversed segment, swapping start and end and reversing the controls.</summary>
    /// <returns>The reversed segment.</returns>
    internal abstract SvgSegment Reverse();

    /// <summary>Creates the segment with every defining point transformed.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>The transformed segment.</returns>
    internal abstract SvgSegment Transform(SvgAffine transform);

    /// <summary>
    /// Appends the segment's flattened interior points and its end point, in that order, to a
    /// contour polyline. The segment's start point is not appended because the caller already added
    /// it as the previous segment's end (or as the contour start).
    /// </summary>
    /// <param name="points">The point list to append to.</param>
    /// <param name="tolerance">The maximum permitted chord deviation, greater than zero.</param>
    internal abstract void AppendPolylinePoints(List<SvgPoint> points, double tolerance);

    /// <summary>Grows the bounds so they contain the segment's exact extent.</summary>
    /// <param name="bounds">The bounds to grow.</param>
    internal abstract void IncludeBounds(ref SvgBounds bounds);

    /// <summary>Computes how many flattened pieces a curve needs for the requested tolerance.</summary>
    /// <param name="deviation">The curve's maximum deviation from its chord.</param>
    /// <param name="tolerance">The maximum permitted chord deviation, greater than zero.</param>
    /// <returns>The piece count, at least one and at most <see cref="MaximumFlatteningSteps"/>.</returns>
    /// <remarks>
    /// Subdividing a curve into <c>n</c> equal parameter intervals divides its second difference by
    /// <c>n²</c>, so <c>n = ceiling(sqrt(deviation / tolerance))</c> meets the tolerance for the
    /// quadratic and cubic deviation bounds used by the segment types.
    /// </remarks>
    protected static int FlatteningStepCount(double deviation, double tolerance)
    {
        if (!(deviation > tolerance))
        {
            return 1;
        }

        double steps = Math.Ceiling(Math.Sqrt(deviation / tolerance));
        return (int)Math.Clamp(steps, 1, MaximumFlatteningSteps);
    }
}
