using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The world-unit quad geometry resolved from frozen <see cref="SpriteRenderData"/> (M8 §6.4).
/// </summary>
/// <remarks>
/// <para>
/// <c>SizeX</c>/<c>SizeY</c> already include the <c>FlipX</c>/<c>FlipY</c> sign, so the vertex
/// shader's <c>(c.x - pivot.x) * size.x</c> / <c>(pivot.y - c.y) * size.y</c> form is algebraically
/// identical to the approved <c>size * sx</c>/<c>size * sy</c> formula while the instance
/// <c>flags</c> field stays reserved and zero.
/// </para>
/// <para>
/// Geometry comes only from the normalized UV span and the per-axis world scale: a full UV quad is
/// <c>(maxU - minU) * Scale.X</c> by <c>(maxV - minV) * Scale.Y</c> world units. Texture pixel
/// dimensions are never consulted.
/// </para>
/// <para>
/// A malformed field is a fatal renderer error naming the field and the frozen texture reference; a
/// known non-<c>Quad</c> topology is a non-fatal skip handled by
/// <see cref="SpritePlanning.Classify"/> and never reaches this resolver.
/// </para>
/// </remarks>
/// <param name="WorldWidth">The unsigned world-unit width.</param>
/// <param name="WorldHeight">The unsigned world-unit height.</param>
/// <param name="SizeX">The signed instance sizeX (negative when flipped horizontally).</param>
/// <param name="SizeY">The signed instance sizeY (negative when flipped vertically).</param>
/// <param name="Pivot">The normalized top-left-origin pivot.</param>
internal readonly record struct SpriteQuad(
    float WorldWidth,
    float WorldHeight,
    float SizeX,
    float SizeY,
    Vector2 Pivot)
{
    /// <summary>
    /// Validates a <c>Quad</c> sprite's frozen fields and resolves its world-unit geometry.
    /// </summary>
    /// <param name="data">The frozen sprite render data; must be a <c>Quad</c>.</param>
    /// <param name="flipX">Whether the geometry is mirrored horizontally around the pivot.</param>
    /// <param name="flipY">Whether the geometry is mirrored vertically around the pivot.</param>
    /// <returns>The resolved quad.</returns>
    /// <exception cref="Bork2dException">A sprite field is malformed.</exception>
    internal static SpriteQuad Resolve(SpriteRenderData data, bool flipX, bool flipY)
    {
        ArgumentNullException.ThrowIfNull(data);

        Vector2[]? uvs = data.Uvs;
        if (uvs is null)
        {
            throw CreateMalformedException(data, "Uvs", "the UV array is null");
        }

        if (uvs.Length != 4)
        {
            throw CreateMalformedException(
                data,
                "Uvs",
                $"expected exactly four UVs, got {uvs.Length}");
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
                throw CreateMalformedException(
                    data,
                    "Uvs",
                    $"UV[{index}] is not finite");
            }

            minU = MathF.Min(minU, uv.X);
            maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y);
            maxV = MathF.Max(maxV, uv.Y);
        }

        Vector2 scale = data.Scale;
        if (!float.IsFinite(scale.X) || scale.X <= 0f || !float.IsFinite(scale.Y) || scale.Y <= 0f)
        {
            throw CreateMalformedException(
                data,
                "Scale",
                $"expected a finite world scale greater than zero on both axes, got ({scale.X}, {scale.Y})");
        }

        Vector2 pivot = data.Pivot;
        if (!float.IsFinite(pivot.X)
            || !float.IsFinite(pivot.Y)
            || pivot.X < 0f
            || pivot.X > 1f
            || pivot.Y < 0f
            || pivot.Y > 1f)
        {
            throw CreateMalformedException(
                data,
                "Pivot",
                $"expected a finite normalized pivot in [0,1], got ({pivot.X}, {pivot.Y})");
        }

        if (data.Topology != SpriteTopology.Quad)
        {
            throw CreateMalformedException(
                data,
                "Topology",
                $"the sprite resolver only accepts Quad, got {data.Topology}");
        }

        if (data.Indices is not null)
        {
            throw CreateMalformedException(
                data,
                "Indices",
                "a Quad must have a null index buffer");
        }

        float worldWidth = (maxU - minU) * scale.X;
        float worldHeight = (maxV - minV) * scale.Y;
        return new SpriteQuad(
            WorldWidth: worldWidth,
            WorldHeight: worldHeight,
            SizeX: flipX ? -worldWidth : worldWidth,
            SizeY: flipY ? -worldHeight : worldHeight,
            Pivot: pivot);
    }

    /// <summary>
    /// Creates the fatal renderer exception for a malformed sprite field, naming the field and the
    /// frozen texture reference (M8 §6.4).
    /// </summary>
    /// <param name="data">The malformed frozen sprite data.</param>
    /// <param name="field">The field name.</param>
    /// <param name="detail">The concrete problem.</param>
    /// <returns>The exception to throw.</returns>
    internal static Bork2dException CreateMalformedException(
        SpriteRenderData data,
        string field,
        string detail)
    {
        string texture = data.Texture.Path is { Length: > 0 } path ? path : "<none>";
        return new Bork2dException(
            $"Sprite asset '{texture}' has a malformed '{field}' field: {detail}.");
    }
}
