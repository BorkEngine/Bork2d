namespace Bork2d.Bake.Svg;

/// <summary>
/// Normalizes cleaned contours to the nonzero representation used by the MSDF baker.
/// </summary>
/// <remarks>
/// <para>
/// Every contour is flattened once at the caller's tolerance and its signed area is computed in the
/// artboard-local Y-up space, where a positive area is the desired outer orientation. A nonzero shape
/// keeps the authored relative winding and is globally reversed when its largest-absolute-area
/// contour is negative. An even-odd shape has every contour oriented by its nesting depth: even
/// depths become positive and odd depths negative.
/// </para>
/// <para>
/// The subset limitation is deliberate: self-intersecting or overlapping subpaths keep whatever
/// orientation this rule assigns and are not an error. Only holes formed by nested subpaths are
/// guaranteed to be represented.
/// </para>
/// </remarks>
internal static class SvgShapeNormalizer
{
    /// <summary>Normalizes one shape's contours.</summary>
    /// <param name="contours">The cleaned, closed contours in artboard-local Y-up coordinates.</param>
    /// <param name="fillRule">The shape's authored fill rule.</param>
    /// <param name="tolerance">The flattening tolerance used for areas and nesting; greater than zero.</param>
    /// <returns>The oriented contours, in the original order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tolerance"/> is not finite or not greater than zero.
    /// </exception>
    internal static IReadOnlyList<SvgContour> Normalize(
        IReadOnlyList<SvgContour> contours,
        SvgFillRule fillRule,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(contours);
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                tolerance,
                "The flattening tolerance must be finite and greater than zero.");
        }

        if (contours.Count == 0)
        {
            return [];
        }

        var polygons = new List<SvgPoint>[contours.Count];
        var areas = new double[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            polygons[index] = [.. contours[index].ToPolyline(tolerance)];
            areas[index] = SvgPolygon.SignedArea(polygons[index]);
        }

        return fillRule == SvgFillRule.EvenOdd
            ? NormalizeEvenOdd(contours, polygons, areas)
            : NormalizeNonZero(contours, areas);
    }

    private static IReadOnlyList<SvgContour> NormalizeNonZero(
        IReadOnlyList<SvgContour> contours,
        double[] areas)
    {
        int largest = 0;
        for (int index = 1; index < areas.Length; index++)
        {
            if (Math.Abs(areas[index]) > Math.Abs(areas[largest]))
            {
                largest = index;
            }
        }

        if (areas[largest] >= 0)
        {
            return [.. contours];
        }

        // Reversing every contour flips every sign, so the authored hole relationship is preserved.
        var reversed = new SvgContour[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            reversed[index] = contours[index].Reverse();
        }

        return reversed;
    }

    private static IReadOnlyList<SvgContour> NormalizeEvenOdd(
        IReadOnlyList<SvgContour> contours,
        List<SvgPoint>[] polygons,
        double[] areas)
    {
        var normalized = new SvgContour[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            int depth = 0;
            if (SvgPolygon.TryFindInteriorPoint(polygons[index], out SvgPoint point))
            {
                for (int other = 0; other < polygons.Length; other++)
                {
                    if (other != index && SvgPolygon.Contains(polygons[other], point))
                    {
                        depth++;
                    }
                }
            }

            bool shouldBePositive = depth % 2 == 0;
            bool isPositive = areas[index] > 0;
            normalized[index] = isPositive == shouldBePositive
                ? contours[index]
                : contours[index].Reverse();
        }

        return normalized;
    }
}
