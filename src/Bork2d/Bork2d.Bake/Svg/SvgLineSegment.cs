namespace Bork2d.Bake.Svg;

/// <summary>
/// A straight line segment.
/// </summary>
internal sealed class SvgLineSegment : SvgSegment
{
    /// <summary>Initializes a new instance of the <see cref="SvgLineSegment"/> class.</summary>
    /// <param name="start">The line start point.</param>
    /// <param name="end">The line end point.</param>
    internal SvgLineSegment(SvgPoint start, SvgPoint end)
        : base(start, end)
    {
    }

    /// <inheritdoc/>
    internal override SvgPoint PointAt(double t)
        => new(Start.X + ((End.X - Start.X) * t), Start.Y + ((End.Y - Start.Y) * t));

    /// <inheritdoc/>
    internal override SvgSegment Reverse() => new SvgLineSegment(End, Start);

    /// <inheritdoc/>
    internal override SvgSegment Transform(SvgAffine transform)
        => new SvgLineSegment(transform.Transform(Start), transform.Transform(End));

    /// <inheritdoc/>
    internal override void AppendPolylinePoints(List<SvgPoint> points, double tolerance) => points.Add(End);

    /// <inheritdoc/>
    internal override void IncludeBounds(ref SvgBounds bounds)
    {
        bounds.Include(Start);
        bounds.Include(End);
    }
}
