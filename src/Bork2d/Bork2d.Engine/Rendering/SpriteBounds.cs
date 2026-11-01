using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Pure, allocation-free world-bounds math for sprite quads.
/// </summary>
/// <remarks>
/// <para>
/// The helper uses the same quad formula as the sprite vertex shader:
/// <c>local = ((corner.x - pivot.x) * sizeX, (pivot.y - corner.y) * sizeY)</c> for the four
/// <c>(0/1, 0/1)</c> corners, where <c>sizeX</c>/<c>sizeY</c> are the signed world width and height
/// (the flip sign is part of the per-axis scale). All four corners are transformed by the world
/// matrix and reduced to an AABB, so rotated, mirrored, sheared, non-square, and pivoted sprites
/// produce a conservative box.
/// </para>
/// <para>
/// Field validation mirrors <c>SpriteQuad.Resolve</c>, but a malformed or absent quad reports
/// <see cref="Bounds.Empty"/> instead of throwing: the public <c>ISpriteRender.Bounds</c> getter
/// must never throw. The live renderable and the capture definition share this helper, so the
/// published snapshot bounds and the live bounds use exactly the same math.
/// </para>
/// </remarks>
internal static class SpriteBounds
{
    /// <summary>
    /// Computes the world-space AABB of a drawn quad from frozen render data, flips, and world
    /// matrix.
    /// </summary>
    /// <param name="data">The frozen sprite render data, or <see langword="null"/>.</param>
    /// <param name="world">The sprite's local-to-world matrix.</param>
    /// <param name="flipX">Whether the quad is mirrored horizontally around the pivot.</param>
    /// <param name="flipY">Whether the quad is mirrored vertically around the pivot.</param>
    /// <returns>
    /// The world-space AABB, or <see cref="Bounds.Empty"/> when the sprite is absent or malformed.
    /// </returns>
    internal static Bounds CreateWorldBounds(
        SpriteRenderData? data,
        in Matrix3x2 world,
        bool flipX,
        bool flipY)
    {
        if (data is null)
        {
            return Bounds.Empty;
        }

        Vector2[]? uvs = data.Uvs;
        if (uvs is null || uvs.Length != 4)
        {
            return Bounds.Empty;
        }

        float minU = float.PositiveInfinity;
        float maxU = float.NegativeInfinity;
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        for (var index = 0; index < uvs.Length; index++)
        {
            Vector2 uv = uvs[index];
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                return Bounds.Empty;
            }

            minU = MathF.Min(minU, uv.X);
            maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y);
            maxV = MathF.Max(maxV, uv.Y);
        }

        Vector2 scale = data.Scale;
        if (!float.IsFinite(scale.X) || scale.X <= 0f || !float.IsFinite(scale.Y) || scale.Y <= 0f)
        {
            return Bounds.Empty;
        }

        Vector2 pivot = data.Pivot;
        if (!float.IsFinite(pivot.X)
            || !float.IsFinite(pivot.Y)
            || pivot.X < 0f
            || pivot.X > 1f
            || pivot.Y < 0f
            || pivot.Y > 1f)
        {
            return Bounds.Empty;
        }

        if (data.Topology != SpriteTopology.Quad || data.Indices is not null)
        {
            return Bounds.Empty;
        }

        float worldWidth = (maxU - minU) * scale.X;
        float worldHeight = (maxV - minV) * scale.Y;
        float sizeX = flipX ? -worldWidth : worldWidth;
        float sizeY = flipY ? -worldHeight : worldHeight;

        Vector2 corner0 = TransformCorner(0f, 0f, pivot, sizeX, sizeY, world);
        Vector2 corner1 = TransformCorner(1f, 0f, pivot, sizeX, sizeY, world);
        Vector2 corner2 = TransformCorner(0f, 1f, pivot, sizeX, sizeY, world);
        Vector2 corner3 = TransformCorner(1f, 1f, pivot, sizeX, sizeY, world);
        Bounds bounds = Bounds.FromMinMax(
            Vector2.Min(Vector2.Min(corner0, corner1), Vector2.Min(corner2, corner3)),
            Vector2.Max(Vector2.Max(corner0, corner1), Vector2.Max(corner2, corner3)));

        // A collapsed or non-finite world matrix has no drawable area; normalize it to the Empty
        // sentinel so callers can compare against Bounds.Empty as well as check IsEmpty.
        return bounds.IsEmpty ? Bounds.Empty : bounds;
    }

    private static Vector2 TransformCorner(
        float cornerX,
        float cornerY,
        Vector2 pivot,
        float sizeX,
        float sizeY,
        in Matrix3x2 world)
    {
        var local = new Vector2(
            (cornerX - pivot.X) * sizeX,
            (pivot.Y - cornerY) * sizeY);
        return Vector2.Transform(local, world);
    }
}
