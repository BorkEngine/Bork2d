namespace Bork2d.Bake.Svg;

/// <summary>
/// Normalizes cleaned contours to the nonzero representation used by the MSDF baker.
/// </summary>
/// <remarks>
/// <para>
/// Every contour is flattened once at the caller's tolerance and its signed area is computed in the
/// artboard-local Y-up space, where a positive area is the desired outer orientation.
/// </para>
/// <para>
/// A nonzero shape is normalized per containment root component. The parent of a contour is the
/// smallest-absolute-area contour whose interior point contains it; a contour without such a parent
/// is a root. A negative root reverses together with its whole subtree, so every root becomes positive
/// while the authored relative winding inside the component survives (a negative root with a positive
/// hole becomes a positive root with a negative hole). A positive root keeps its subtree as authored.
/// </para>
/// <para>
/// An even-odd shape has every contour oriented by its nesting depth: even depths become positive and
/// odd depths negative.
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
            : NormalizeNonZero(contours, polygons, areas);
    }

    /// <summary>
    /// Makes every containment root positive while preserving the authored winding inside each root's
    /// subtree.
    /// </summary>
    /// <remarks>
    /// A nonzero fill keeps an isolated contour with negative winding filled, so each disconnected
    /// component needs its own orientation fix rather than one global reversal. The containment tree
    /// uses the interior point of each contour and picks the smallest-absolute-area containing contour
    /// as its parent; a contour with no interior point is treated as a root. Reversing a whole subtree
    /// flips every sign in that component, so only the component's outer orientation changes.
    /// </remarks>
    private static IReadOnlyList<SvgContour> NormalizeNonZero(
        IReadOnlyList<SvgContour> contours,
        List<SvgPoint>[] polygons,
        double[] areas)
    {
        int[] parents = BuildParentMap(polygons, areas);
        var children = new List<int>[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            children[index] = [];
        }

        for (int index = 0; index < contours.Count; index++)
        {
            if (parents[index] >= 0)
            {
                children[parents[index]].Add(index);
            }
        }

        var reversed = new bool[contours.Count];
        var pending = new Stack<int>();
        for (int index = 0; index < contours.Count; index++)
        {
            bool isNegativeRoot = parents[index] < 0 && areas[index] < 0;
            if (!isNegativeRoot)
            {
                continue;
            }

            pending.Push(index);
            while (pending.Count > 0)
            {
                int node = pending.Pop();
                if (reversed[node])
                {
                    continue;
                }

                reversed[node] = true;
                foreach (int child in children[node])
                {
                    pending.Push(child);
                }
            }
        }

        var normalized = new SvgContour[contours.Count];
        for (int index = 0; index < contours.Count; index++)
        {
            normalized[index] = reversed[index] ? contours[index].Reverse() : contours[index];
        }

        return normalized;
    }

    /// <summary>
    /// Maps each contour to the smallest-absolute-area contour that contains it, or to
    /// <c>-1</c> when it is a root.
    /// </summary>
    /// <remarks>
    /// A parent must have a strictly larger absolute area, so two coincident contours cannot parent
    /// each other and the relation stays acyclic. A contour whose interior point cannot be found is a
    /// root.
    /// </remarks>
    private static int[] BuildParentMap(List<SvgPoint>[] polygons, double[] areas)
    {
        var parents = new int[polygons.Length];
        for (int index = 0; index < polygons.Length; index++)
        {
            parents[index] = -1;
            if (!SvgPolygon.TryFindInteriorPoint(polygons[index], out SvgPoint point))
            {
                continue;
            }

            double ownArea = Math.Abs(areas[index]);
            double smallestParentArea = double.PositiveInfinity;
            for (int candidate = 0; candidate < polygons.Length; candidate++)
            {
                if (candidate == index)
                {
                    continue;
                }

                double candidateArea = Math.Abs(areas[candidate]);
                if (candidateArea <= ownArea || candidateArea >= smallestParentArea)
                {
                    continue;
                }

                if (SvgPolygon.Contains(polygons[candidate], point))
                {
                    parents[index] = candidate;
                    smallestParentArea = candidateArea;
                }
            }
        }

        return parents;
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
