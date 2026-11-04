using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Pure, allocation-free world-bounds math for line geometry.
/// </summary>
/// <remarks>
/// <para>
/// The local-space geometry bounds already include half the line width and every miter or bevel
/// extent, so the world AABB is the AABB of the four transformed local-AABB corners. The helper is
/// conservative for rotation, mirror, shear, and non-uniform scale.
/// </para>
/// <para>
/// A malformed or absent geometry and a collapsed or non-finite world matrix report
/// <see cref="Bounds.Empty"/> instead of throwing, because the public
/// <c>IBoundedTransform.Bounds</c> getter must never throw. The live renderable and the capture
/// definition share this helper, so the published snapshot bounds and the live bounds use exactly
/// the same math.
/// </para>
/// </remarks>
internal static class LineBounds
{
    /// <summary>
    /// Computes the world-space AABB of a line's local geometry.
    /// </summary>
    /// <param name="geometry">The cached local geometry, or <see langword="null"/>.</param>
    /// <param name="world">The line's local-to-world matrix.</param>
    /// <returns>
    /// The world-space AABB, or <see cref="Bounds.Empty"/> when the line is not drawable.
    /// </returns>
    internal static Bounds CreateWorldBounds(LineGeometryData? geometry, in Matrix3x2 world)
    {
        if (geometry is null)
        {
            return Bounds.Empty;
        }

        Bounds local = geometry.Bounds;
        Vector2 min = local.Min;
        Vector2 max = local.Max;

        Vector2 corner0 = Vector2.Transform(new Vector2(min.X, min.Y), world);
        Vector2 corner1 = Vector2.Transform(new Vector2(max.X, min.Y), world);
        Vector2 corner2 = Vector2.Transform(new Vector2(min.X, max.Y), world);
        Vector2 corner3 = Vector2.Transform(new Vector2(max.X, max.Y), world);
        if (!IsFinite(corner0) || !IsFinite(corner1) || !IsFinite(corner2) || !IsFinite(corner3))
        {
            return Bounds.Empty;
        }

        Bounds bounds = Bounds.FromMinMax(
            Vector2.Min(Vector2.Min(corner0, corner1), Vector2.Min(corner2, corner3)),
            Vector2.Max(Vector2.Max(corner0, corner1), Vector2.Max(corner2, corner3)));

        // A collapsed or non-finite world matrix has no drawable area; normalize it to the Empty
        // sentinel so callers can compare against Bounds.Empty as well as check IsEmpty.
        return bounds.IsEmpty ? Bounds.Empty : bounds;
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
