namespace Bork2d.Bake.Svg;

/// <summary>
/// A cubic Bézier segment.
/// </summary>
internal sealed class SvgCubicSegment : SvgSegment
{
    /// <summary>Initializes a new instance of the <see cref="SvgCubicSegment"/> class.</summary>
    /// <param name="start">The segment start point.</param>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="end">The segment end point.</param>
    internal SvgCubicSegment(SvgPoint start, SvgPoint control1, SvgPoint control2, SvgPoint end)
        : base(start, end)
    {
        Control1 = control1;
        Control2 = control2;
    }

    /// <summary>Gets the first control point.</summary>
    internal SvgPoint Control1 { get; }

    /// <summary>Gets the second control point.</summary>
    internal SvgPoint Control2 { get; }

    /// <inheritdoc/>
    internal override SvgPoint PointAt(double t)
    {
        double inverse = 1 - t;
        double startWeight = inverse * inverse * inverse;
        double control1Weight = 3 * inverse * inverse * t;
        double control2Weight = 3 * inverse * t * t;
        double endWeight = t * t * t;

        return new SvgPoint(
            (startWeight * Start.X) + (control1Weight * Control1.X) + (control2Weight * Control2.X)
                + (endWeight * End.X),
            (startWeight * Start.Y) + (control1Weight * Control1.Y) + (control2Weight * Control2.Y)
                + (endWeight * End.Y));
    }

    /// <inheritdoc/>
    internal override SvgSegment Reverse() => new SvgCubicSegment(End, Control2, Control1, Start);

    /// <inheritdoc/>
    internal override SvgSegment Transform(SvgAffine transform)
        => new SvgCubicSegment(
            transform.Transform(Start),
            transform.Transform(Control1),
            transform.Transform(Control2),
            transform.Transform(End));

    /// <inheritdoc/>
    internal override void AppendPolylinePoints(List<SvgPoint> points, double tolerance)
    {
        // A cubic deviates from its chord by at most 3/4 of the larger endpoint second difference.
        double first = Math.Sqrt(
            Math.Pow(Start.X - (2 * Control1.X) + Control2.X, 2)
                + Math.Pow(Start.Y - (2 * Control1.Y) + Control2.Y, 2));
        double second = Math.Sqrt(
            Math.Pow(Control1.X - (2 * Control2.X) + End.X, 2)
                + Math.Pow(Control1.Y - (2 * Control2.Y) + End.Y, 2));
        double deviation = 0.75 * Math.Max(first, second);
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
        IncludeAxisExtremum(ref bounds, Start.X, Control1.X, Control2.X, End.X);
        IncludeAxisExtremum(ref bounds, Start.Y, Control1.Y, Control2.Y, End.Y);
    }

    /// <summary>
    /// Includes the interior points where one coordinate's derivative is zero.
    /// </summary>
    /// <remarks>
    /// The derivative divided by three is the quadratic
    /// <c>a*t² + b*t + c</c> with <c>a = -start + 3*control1 - 3*control2 + end</c>,
    /// <c>b = 2*(start - 2*control1 + control2)</c>, and <c>c = control1 - start</c>.
    /// </remarks>
    private void IncludeAxisExtremum(
        ref SvgBounds bounds,
        double start,
        double control1,
        double control2,
        double end)
    {
        double a = -start + (3 * control1) - (3 * control2) + end;
        double b = 2 * (start - (2 * control1) + control2);
        double c = control1 - start;

        if (a == 0)
        {
            if (b != 0)
            {
                IncludeIfInterior(ref bounds, -c / b);
            }

            return;
        }

        double discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
        {
            return;
        }

        double root = Math.Sqrt(discriminant);
        IncludeIfInterior(ref bounds, (-b + root) / (2 * a));
        IncludeIfInterior(ref bounds, (-b - root) / (2 * a));
    }

    private void IncludeIfInterior(ref SvgBounds bounds, double t)
    {
        if (double.IsFinite(t) && t > 0 && t < 1)
        {
            bounds.Include(PointAt(t));
        }
    }
}
