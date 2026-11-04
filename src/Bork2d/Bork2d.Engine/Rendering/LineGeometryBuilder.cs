using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Pure CPU expansion of a polyline into local-space line triangles.
/// </summary>
/// <remarks>
/// <para>
/// The builder never throws for malformed input: a non-finite point, a non-finite or non-positive
/// width, a zero-length segment, or too few distinct points returns <see langword="null"/>, which
/// the renderable treats as "not drawable". Duplicate consecutive points are skipped, and a closed
/// line drops a final point equal to the first before joining the last point to the first.
/// </para>
/// <para>
/// Each segment becomes four vertices and two triangles. Open ends are butt caps with no extension
/// past the endpoints. Interior corners use a miter join while the miter distance stays within
/// <see cref="MiterLimit"/> times the half-width; a corner beyond the limit falls back to a bevel
/// triangle on the outer side. Closed lines join every vertex, including the last-to-first corner,
/// and have no caps.
/// </para>
/// <para>
/// The per-vertex line UV <c>x</c> is <c>0</c> at the left edge and <c>1</c> at the right edge
/// across the width; <c>y</c> is the accumulated local-space distance along the line. Self-
/// intersecting polylines are not special-cased.
/// </para>
/// </remarks>
internal static class LineGeometryBuilder
{
    /// <summary>
    /// The maximum miter distance as a multiple of the half-width. A larger miter becomes a bevel.
    /// </summary>
    internal const float MiterLimit = 4f;

    private const float MinimumMiterDenominator = 1e-6f;

    /// <summary>
    /// Expands a polyline into local-space triangles, or returns <see langword="null"/> when the
    /// input is not drawable.
    /// </summary>
    /// <param name="points">The centerline points in local units.</param>
    /// <param name="closed">Whether the last point joins the first.</param>
    /// <param name="width">The full line width in local units; must be finite and positive.</param>
    /// <returns>The immutable geometry, or <see langword="null"/> when nothing can be drawn.</returns>
    internal static LineGeometryData? Build(ReadOnlySpan<Vector2> points, bool closed, float width)
    {
        if (!float.IsFinite(width) || width <= 0f)
        {
            return null;
        }

        var distinct = new List<Vector2>(points.Length);
        foreach (Vector2 point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                return null;
            }

            if (distinct.Count > 0 && point == distinct[^1])
            {
                continue;
            }

            distinct.Add(point);
        }

        // A closed polyline that repeats its first point at the end is the same loop; drop the
        // duplicate before deciding whether enough distinct points remain.
        if (closed && distinct.Count > 1 && distinct[^1] == distinct[0])
        {
            distinct.RemoveAt(distinct.Count - 1);
        }

        int pointCount = distinct.Count;
        if (closed ? pointCount < 3 : pointCount < 2)
        {
            return null;
        }

        int segmentCount = closed ? pointCount : pointCount - 1;
        var directions = new Vector2[segmentCount];
        var normals = new Vector2[segmentCount];
        var distances = new float[pointCount];
        var totalDistance = 0f;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            Vector2 from = distinct[segment];
            Vector2 to = distinct[(segment + 1) % pointCount];
            Vector2 delta = to - from;
            float length = delta.Length();
            if (!(length > 0f) || !float.IsFinite(length))
            {
                return null;
            }

            var direction = delta / length;
            if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
            {
                return null;
            }

            directions[segment] = direction;
            normals[segment] = new Vector2(-direction.Y, direction.X);
            if (segment + 1 < pointCount)
            {
                distances[segment + 1] = distances[segment] + length;
            }

            totalDistance += length;
            if (!float.IsFinite(totalDistance))
            {
                return null;
            }
        }

        var miterLeft = new Vector2[pointCount];
        var miterRight = new Vector2[pointCount];
        var hasMiter = new bool[pointCount];
        for (var point = 0; point < pointCount; point++)
        {
            if (!IsJoint(point, pointCount, closed))
            {
                continue;
            }

            int previous = (point - 1 + pointCount) % pointCount;
            int next = point % pointCount;
            if (TryCreateMiter(
                directions[previous],
                directions[next],
                width * 0.5f,
                out Vector2 left,
                out Vector2 right))
            {
                miterLeft[point] = left;
                miterRight[point] = right;
                hasMiter[point] = true;
            }
        }

        var halfWidth = width * 0.5f;
        var vertices = new List<Vector2>((segmentCount * 4) + (pointCount * 3));
        var lineUvs = new List<Vector2>(vertices.Capacity);
        var indices = new List<uint>(vertices.Capacity);

        for (var segment = 0; segment < segmentCount; segment++)
        {
            Vector2 start = distinct[segment];
            Vector2 end = distinct[(segment + 1) % pointCount];
            Vector2 normal = normals[segment] * halfWidth;
            Vector2 startLeft;
            Vector2 startRight;
            if ((closed || segment > 0) && hasMiter[segment])
            {
                startLeft = start + miterLeft[segment];
                startRight = start + miterRight[segment];
            }
            else
            {
                startLeft = start + normal;
                startRight = start - normal;
            }

            int endIndex = (segment + 1) % pointCount;
            Vector2 endLeft;
            Vector2 endRight;
            if ((closed || segment + 1 < segmentCount) && hasMiter[endIndex])
            {
                endLeft = end + miterLeft[endIndex];
                endRight = end + miterRight[endIndex];
            }
            else
            {
                endLeft = end + normal;
                endRight = end - normal;
            }

            float startDistance = distances[segment];
            float endDistance = segment + 1 < pointCount ? distances[segment + 1] : totalDistance;
            AddQuad(
                vertices,
                lineUvs,
                indices,
                startLeft,
                startRight,
                endLeft,
                endRight,
                startDistance,
                endDistance);
        }

        for (var point = 0; point < pointCount; point++)
        {
            if (!IsJoint(point, pointCount, closed) || hasMiter[point])
            {
                continue;
            }

            int previous = (point - 1 + pointCount) % pointCount;
            int next = point % pointCount;
            float turn = Cross(directions[previous], directions[next]);
            if (turn == 0f)
            {
                // A 180-degree reversal has no outer side; the two segments cover the same area.
                continue;
            }

            float side = turn > 0f ? -1f : 1f;
            Vector2 first = distinct[point] + (normals[previous] * (side * halfWidth));
            Vector2 second = distinct[point] + (normals[next] * (side * halfWidth));
            AddBevel(
                vertices,
                lineUvs,
                indices,
                distinct[point],
                first,
                second,
                distances[point],
                turn < 0f,
                turn > 0f);
        }

        if (vertices.Count == 0)
        {
            return null;
        }

        Bounds bounds = ComputeBounds(vertices);
        return bounds.IsEmpty
            ? null
            : new LineGeometryData(vertices.ToArray(), lineUvs.ToArray(), indices.ToArray(), bounds);
    }

    private static bool IsJoint(int point, int pointCount, bool closed)
        => closed || (point > 0 && point + 1 < pointCount);

    private static bool TryCreateMiter(
        Vector2 previousDirection,
        Vector2 nextDirection,
        float halfWidth,
        out Vector2 left,
        out Vector2 right)
    {
        float denominator = 1f + Vector2.Dot(previousDirection, nextDirection);
        if (!(denominator > MinimumMiterDenominator))
        {
            left = default;
            right = default;
            return false;
        }

        var previousNormal = new Vector2(-previousDirection.Y, previousDirection.X);
        var nextNormal = new Vector2(-nextDirection.Y, nextDirection.X);
        Vector2 miter = (previousNormal + nextNormal) / denominator;
        float ratio = miter.Length();
        if (!float.IsFinite(ratio) || ratio > MiterLimit)
        {
            left = default;
            right = default;
            return false;
        }

        left = miter * halfWidth;
        right = -left;
        return true;
    }

    private static void AddQuad(
        List<Vector2> vertices,
        List<Vector2> lineUvs,
        List<uint> indices,
        Vector2 startLeft,
        Vector2 startRight,
        Vector2 endLeft,
        Vector2 endRight,
        float startDistance,
        float endDistance)
    {
        var baseIndex = (uint)vertices.Count;
        vertices.Add(startLeft);
        vertices.Add(startRight);
        vertices.Add(endLeft);
        vertices.Add(endRight);
        lineUvs.Add(new Vector2(0f, startDistance));
        lineUvs.Add(new Vector2(1f, startDistance));
        lineUvs.Add(new Vector2(0f, endDistance));
        lineUvs.Add(new Vector2(1f, endDistance));
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 3);
        indices.Add(baseIndex + 2);
    }

    private static void AddBevel(
        List<Vector2> vertices,
        List<Vector2> lineUvs,
        List<uint> indices,
        Vector2 corner,
        Vector2 first,
        Vector2 second,
        float distance,
        bool leftSide,
        bool leftTurn)
    {
        float edgeUv = leftSide ? 0f : 1f;
        var baseIndex = (uint)vertices.Count;
        vertices.Add(corner);
        lineUvs.Add(new Vector2(0.5f, distance));
        if (leftTurn)
        {
            vertices.Add(first);
            vertices.Add(second);
        }
        else
        {
            vertices.Add(second);
            vertices.Add(first);
        }

        lineUvs.Add(new Vector2(edgeUv, distance));
        lineUvs.Add(new Vector2(edgeUv, distance));
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
    }

    private static Bounds ComputeBounds(List<Vector2> vertices)
    {
        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        for (var index = 1; index < vertices.Count; index++)
        {
            Vector2 vertex = vertices[index];
            min = Vector2.Min(min, vertex);
            max = Vector2.Max(max, vertex);
        }

        Bounds bounds = Bounds.FromMinMax(min, max);
        return bounds.IsEmpty ? Bounds.Empty : bounds;
    }

    private static float Cross(Vector2 first, Vector2 second)
        => (first.X * second.Y) - (first.Y * second.X);
}
