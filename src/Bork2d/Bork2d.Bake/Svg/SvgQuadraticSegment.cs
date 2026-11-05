namespace Bork2d.Bake.Svg;

/// <summary>
/// A quadratic Bézier segment.
/// </summary>
internal sealed class SvgQuadraticSegment : SvgSegment
{
    /// <summary>Initializes a new instance of the <see cref="SvgQuadraticSegment"/> class.</summary>
    /// <param name="start">The segment start point.</param>
    /// <param name="control">The single control point.</param>
    /// <param name="end">The segment end point.</param>
    internal SvgQuadraticSegment(SvgPoint start, SvgPoint control, SvgPoint end)
        : base(start, end)
    {
        Control = control;
    }

    /// <summary>Gets the single control point.</summary>
    internal SvgPoint Control { get; }

    /// <inheritdoc/>
    internal override SvgPoint PointAt(double t)
    {
        double inverse = 1 - t;
        double startWeight = inverse * inverse;
        double controlWeight = 2 * inverse * t;
        double endWeight = t * t;

        return new SvgPoint(
            (startWeight * Start.X) + (controlWeight * Control.X) + (endWeight * End.X),
            (startWeight * Start.Y) + (controlWeight * Control.Y) + (endWeight * End.Y));
    }

    /// <inheritdoc/>
    internal override SvgSegment Reverse() => new SvgQuadraticSegment(End, Control, Start);

    /// <inheritdoc/>
    internal override bool IsFinite()
        => IsFinitePoint(Start) && IsFinitePoint(Control) && IsFinitePoint(End);

    /// <inheritdoc/>
    internal override SvgSegment Transform(SvgAffine transform)
        => new SvgQuadraticSegment(
            transform.Transform(Start),
            transform.Transform(Control),
            transform.Transform(End));

    /// <inheritdoc/>
    internal override void AppendPolylinePoints(List<SvgPoint> points, double tolerance)
    {
        // A quadratic deviates from its chord by at most |Start - 2*Control + End| / 4.
        double deviation =
            Math.Sqrt(
                Math.Pow(Start.X - (2 * Control.X) + End.X, 2)
                    + Math.Pow(Start.Y - (2 * Control.Y) + End.Y, 2))
            / 4;
        int steps = FlatteningStepCount(deviation, tolerance);
        for (int step = 1; step < steps; step++)
        {
            points.Add(PointAt((double)step / steps));
        }

        points.Add(End);
    }

    /// <inheritdoc/>
    internal override void IncludeBounds(ref SvgBounds bounds)
    {
        bounds.Include(Start);
        bounds.Include(End);
        IncludeAxisExtremum(ref bounds, Start.X, Control.X, End.X);
        IncludeAxisExtremum(ref bounds, Start.Y, Control.Y, End.Y);
    }

    /// <summary>
    /// Includes the point where one coordinate's derivative is zero. The derivative is
    /// <c>2*((1-t)*(control - start) + t*(end - control))</c>, so a coordinate is stationary at
    /// <c>t = (start - control) / (start - 2*control + end)</c>.
    /// </summary>
    private void IncludeAxisExtremum(ref SvgBounds bounds, double start, double control, double end)
    {
        double denominator = start - (2 * control) + end;
        if (denominator == 0)
        {
            return;
        }

        double t = (start - control) / denominator;
        if (t > 0 && t < 1)
        {
            bounds.Include(PointAt(t));
        }
    }
}
