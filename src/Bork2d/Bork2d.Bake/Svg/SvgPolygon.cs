namespace Bork2d.Bake.Svg;

/// <summary>
/// Polygon helpers used by the orientation normalization: signed area, point containment, and an
/// interior point.
/// </summary>
/// <remarks>
/// Every input is a flattened closed polyline with no repeated closing vertex, so an edge exists
/// from the last point back to the first. The helpers are deterministic and do not depend on the
/// starting vertex.
/// </remarks>
internal static class SvgPolygon
{
    /// <summary>Computes the signed area of a closed polyline with the shoelace formula.</summary>
    /// <param name="points">The polyline vertices, without a repeated closing vertex.</param>
    /// <returns>
    /// The signed area: positive for a counter-clockwise winding in a Y-up space, negative for a
    /// clockwise winding.
    /// </returns>
    internal static double SignedArea(IReadOnlyList<SvgPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            return 0;
        }

        double sum = 0;
        for (int index = 0; index < points.Count; index++)
        {
            SvgPoint current = points[index];
            SvgPoint next = points[(index + 1) % points.Count];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        return sum / 2;
    }

    /// <summary>Tests whether a point lies inside a closed polyline by the even-odd rule.</summary>
    /// <param name="points">The polyline vertices, without a repeated closing vertex.</param>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> when a ray from the point crosses an odd number of edges.</returns>
    /// <remarks>
    /// The vertical comparison is half-open, so a horizontal ray through a vertex counts the vertex
    /// once. Boundary behavior is unspecified; the normalizer only tests points it places inside its
    /// own contour's interior.
    /// </remarks>
    internal static bool Contains(IReadOnlyList<SvgPoint> points, SvgPoint point)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            return false;
        }

        bool inside = false;
        for (int index = 0, previous = points.Count - 1; index < points.Count; previous = index++)
        {
            SvgPoint start = points[previous];
            SvgPoint end = points[index];
            if ((start.Y > point.Y) == (end.Y > point.Y))
            {
                continue;
            }

            double crossingX =
                start.X + ((point.Y - start.Y) * (end.X - start.X) / (end.Y - start.Y));
            if (point.X < crossingX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Finds a point just inside the contour by scanning a horizontal line through its bounds.
    /// </summary>
    /// <param name="points">The polyline vertices, without a repeated closing vertex.</param>
    /// <param name="point">The interior point on success.</param>
    /// <returns><see langword="false"/> when the polyline is degenerate or has no interior span.</returns>
    /// <remarks>
    /// Each candidate line lies halfway between two consecutive distinct vertex Y values, so it never
    /// passes through a vertex, and the returned point sits just inside the first crossing span, next
    /// to the contour's own boundary. Placing the sample next to the boundary rather than at a span's
    /// center keeps it outside contours nested inside this one, so counting the other contours that
    /// contain it measures this contour's nesting depth.
    /// </remarks>
    internal static bool TryFindInteriorPoint(IReadOnlyList<SvgPoint> points, out SvgPoint point)
    {
        ArgumentNullException.ThrowIfNull(points);
        point = default;
        if (points.Count < 3)
        {
            return false;
        }

        double[] levels = [.. points.Select(vertex => vertex.Y).Distinct().OrderBy(value => value)];
        var crossings = new List<double>();
        for (int index = 0; index + 1 < levels.Length; index++)
        {
            double y = (levels[index] + levels[index + 1]) / 2;
            crossings.Clear();
            for (int vertex = 0, previous = points.Count - 1; vertex < points.Count; previous = vertex++)
            {
                SvgPoint start = points[previous];
                SvgPoint end = points[vertex];
                if ((start.Y > y) == (end.Y > y))
                {
                    continue;
                }

                crossings.Add(
                    start.X + ((y - start.Y) * (end.X - start.X) / (end.Y - start.Y)));
            }

            crossings.Sort();
            for (int span = 0; span + 1 < crossings.Count; span += 2)
            {
                double spanStart = crossings[span];
                double spanEnd = crossings[span + 1];
                if (!(spanEnd > spanStart))
                {
                    continue;
                }

                var candidate = new SvgPoint(spanStart + ((spanEnd - spanStart) * 1e-6), y);
                if (Contains(points, candidate))
                {
                    point = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether an area is negligible compared with the polyline's bounding box.
    /// </summary>
    /// <param name="area">The signed area from <see cref="SignedArea"/>.</param>
    /// <param name="points">The flattened polyline the area was computed from.</param>
    /// <returns>
    /// <see langword="true"/> when the magnitude is at most <c>1e-9</c> of the bounding box area, or
    /// at most <c>1e-12</c> when the bounding box itself is degenerate.
    /// </returns>
    /// <remarks>
    /// The threshold is relative to the contour's own size, so a contour that is small only because
    /// of its transform is not dropped, while a collinear or backtracking contour is.
    /// </remarks>
    internal static bool IsNegligibleArea(double area, IReadOnlyList<SvgPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        SvgBounds bounds = SvgBounds.Empty;
        foreach (SvgPoint point in points)
        {
            bounds.Include(point);
        }

        double boxArea = bounds.IsEmpty ? 0 : bounds.Width * bounds.Height;
        double threshold = Math.Max(1e-12, Math.Abs(boxArea) * 1e-9);
        return Math.Abs(area) <= threshold;
    }
}
