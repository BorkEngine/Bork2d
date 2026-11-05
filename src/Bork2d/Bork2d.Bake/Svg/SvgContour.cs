namespace Bork2d.Bake.Svg;

/// <summary>
/// One closed contour of a shape.
/// </summary>
/// <remarks>
/// <para>
/// The segments form a single chain: each segment starts where the previous one ended and the last
/// one ends at the first one's start. The closing point is therefore implicit and never repeated as a
/// duplicate vertex. The constructor enforces that invariant and throws
/// <see cref="ArgumentException"/> when a caller assembles a broken chain, because the parser is the
/// only producer and a broken chain is a programming error rather than malformed SVG input.
/// </para>
/// <para>
/// A contour is immutable and holds no coordinate-space tag; the parser emits artboard-local Y-up
/// points where a positive signed area is the desired outer orientation for a nonzero fill.
/// </para>
/// </remarks>
internal sealed class SvgContour
{
    private readonly SvgSegment[] _segments;

    /// <summary>Initializes a new instance of the <see cref="SvgContour"/> class.</summary>
    /// <param name="segments">The closed segment chain, in drawing order.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="segments"/> is empty, does not chain, or is not closed.
    /// </exception>
    internal SvgContour(IReadOnlyList<SvgSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException("A contour requires at least one segment.", nameof(segments));
        }

        _segments = new SvgSegment[segments.Count];
        for (int index = 0; index < segments.Count; index++)
        {
            SvgSegment segment = segments[index];
            if (index > 0 && _segments[index - 1].End != segment.Start)
            {
                throw new ArgumentException(
                    $"Contour segment {index} does not start where segment {index - 1} ended.",
                    nameof(segments));
            }

            _segments[index] = segment;
        }

        if (_segments[^1].End != _segments[0].Start)
        {
            throw new ArgumentException("A contour must be closed.", nameof(segments));
        }
    }

    /// <summary>Gets the segments in drawing order.</summary>
    internal IReadOnlyList<SvgSegment> Segments => _segments;

    /// <summary>
    /// Flattens the contour into a polyline whose vertices are the flattened segment endpoints.
    /// </summary>
    /// <param name="tolerance">The maximum permitted chord deviation in the contour's units.</param>
    /// <returns>
    /// The polyline vertices, starting at the first segment's start. The polyline is implicitly
    /// closed, so the closing vertex that equals the first point is omitted.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tolerance"/> is not finite or not greater than zero.
    /// </exception>
    /// <remarks>
    /// Flattening subdivides each curve into equal parameter intervals chosen from its chord
    /// deviation, so the result is deterministic and linear in the requested tolerance.
    /// </remarks>
    internal IReadOnlyList<SvgPoint> ToPolyline(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                tolerance,
                "The flattening tolerance must be finite and greater than zero.");
        }

        var points = new List<SvgPoint>();
        points.Add(_segments[0].Start);
        foreach (SvgSegment segment in _segments)
        {
            segment.AppendPolylinePoints(points, tolerance);
        }

        if (points.Count > 1 && points[^1] == points[0])
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    /// <summary>Creates the contour with its winding reversed.</summary>
    /// <returns>A new contour with the same geometry and the opposite signed area.</returns>
    internal SvgContour Reverse()
    {
        var reversed = new SvgSegment[_segments.Length];
        for (int index = 0; index < _segments.Length; index++)
        {
            reversed[index] = _segments[(_segments.Length - 1) - index].Reverse();
        }

        return new SvgContour(reversed);
    }

    /// <summary>Creates the contour with every defining point transformed.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>A new, closed contour in the transformed space.</returns>
    internal SvgContour Transform(SvgAffine transform)
    {
        var transformed = new SvgSegment[_segments.Length];
        for (int index = 0; index < _segments.Length; index++)
        {
            transformed[index] = _segments[index].Transform(transform);
        }

        return new SvgContour(transformed);
    }
}
