namespace Bork2d.Bake.Svg;

/// <summary>
/// Drops the degenerate parts of a raw contour before orientation normalization.
/// </summary>
/// <remarks>
/// <para>
/// The cleaner removes zero-length line segments and duplicate consecutive points (a zero-length
/// line is the only way a chain repeats a point), then drops a contour whose flattened polyline has
/// fewer than three distinct vertices or an area that is negligible against its own bounding box.
/// The flattened vertices are used rather than the segment endpoints because a contour may close a
/// curved arc with a single line and still enclose a real area. Curves whose start equals their end
/// are kept because such a curve can enclose an area; a zero-length curve is not a zero-length line.
/// </para>
/// <para>
/// Cleaning runs in SVG user coordinates, where areas are only affected by the transform's
/// determinant, so the result does not depend on the artboard mapping.
/// </para>
/// </remarks>
internal static class SvgContourCleaner
{
    /// <summary>Cleans one raw contour.</summary>
    /// <param name="contour">The raw, closed contour.</param>
    /// <returns>The cleaned contour, or <see langword="null"/> when it has no fillable geometry.</returns>
    internal static SvgContour? Clean(SvgContour contour)
    {
        ArgumentNullException.ThrowIfNull(contour);

        var segments = new List<SvgSegment>(contour.Segments.Count);
        foreach (SvgSegment segment in contour.Segments)
        {
            if (segment is SvgLineSegment && segment.Start == segment.End)
            {
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var cleaned = new SvgContour(segments);
        SvgBounds bounds = SvgBounds.Empty;
        foreach (SvgSegment segment in cleaned.Segments)
        {
            segment.IncludeBounds(ref bounds);
        }

        // Flatten finely relative to the contour's own size, so a scaled-down shape is judged the
        // same way as its unscaled source.
        double size = Math.Max(bounds.Width, bounds.Height);
        double tolerance = Math.Max(1e-9, size * 1e-6);
        IReadOnlyList<SvgPoint> polygon = cleaned.ToPolyline(tolerance);
        if (polygon.Count < 3)
        {
            return null;
        }

        double area = SvgPolygon.SignedArea(polygon);
        return SvgPolygon.IsNegligibleArea(area, polygon) ? null : cleaned;
    }
}
