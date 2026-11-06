using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Pure, allocation-free world-bounds math for laid-out font glyph quads.
/// </summary>
/// <remarks>
/// <para>
/// The local layout bounds are already the tight AABB of the glyph quads, so the world bounds are
/// the AABB of the four transformed local-AABB corners. The helper is conservative for rotation,
/// mirror, shear, and non-uniform scale, and the live renderable and the capture definition share
/// it so culling sees exactly the quads the renderer draws.
/// </para>
/// <para>
/// An absent or empty layout, a layout with no drawable quads, and a collapsed or non-finite world
/// matrix report <see cref="Bounds.Empty"/> instead of throwing, because the public
/// <c>IBoundedTransform.Bounds</c> getter must never throw for malformed authored data.
/// </para>
/// </remarks>
internal static class FontBounds
{
    /// <summary>
    /// Computes the world-space AABB of laid-out glyph quads.
    /// </summary>
    /// <param name="layout">The local layout, or <see langword="null"/> for none.</param>
    /// <param name="world">The font renderable's local-to-world matrix.</param>
    /// <returns>
    /// The world-space AABB, or <see cref="Bounds.Empty"/> when there are no drawable quads.
    /// </returns>
    internal static Bounds CreateWorldBounds(FontLayoutData? layout, in Matrix3x2 world)
    {
        if (layout is null || layout.IsEmpty)
        {
            return Bounds.Empty;
        }

        Bounds local = layout.LocalBounds;
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
