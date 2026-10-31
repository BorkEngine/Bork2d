using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The world-unit quad geometry resolved from a <see cref="SpriteAsset"/> (M8 §6.4).
/// </summary>
/// <remarks>
/// <para>
/// <c>SizeX</c>/<c>SizeY</c> already include the <c>FlipX</c>/<c>FlipY</c> sign, so the vertex
/// shader's <c>(c.x - pivot.x) * size.x</c> / <c>(pivot.y - c.y) * size.y</c> form is algebraically
/// identical to the approved <c>size * sx</c>/<c>size * sy</c> formula while the instance
/// <c>flags</c> field stays reserved and zero.
/// </para>
/// <para>
/// A malformed field is a fatal renderer error naming the field and the sprite's texture
/// reference; a known non-<c>Quad</c> topology is a non-fatal skip handled by
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
    /// Validates a <c>Quad</c> sprite's fields and resolves its world-unit geometry.
    /// </summary>
    /// <param name="sprite">The loaded sprite asset; must be a <c>Quad</c>.</param>
    /// <param name="textureWidth">The referenced texture's pixel width.</param>
    /// <param name="textureHeight">The referenced texture's pixel height.</param>
    /// <param name="flipX">Whether the geometry is mirrored horizontally around the pivot.</param>
    /// <param name="flipY">Whether the geometry is mirrored vertically around the pivot.</param>
    /// <returns>The resolved quad.</returns>
    /// <exception cref="Bork2dException">A sprite field is malformed.</exception>
    internal static SpriteQuad Resolve(
        SpriteAsset sprite,
        uint textureWidth,
        uint textureHeight,
        bool flipX,
        bool flipY)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        Vector2[]? uvs = sprite.Uvs;
        if (uvs is null)
        {
            throw CreateMalformedException(sprite, "Uvs", "the UV array is null");
        }

        if (uvs.Length != 4)
        {
            throw CreateMalformedException(
                sprite,
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
                    sprite,
                    "Uvs",
                    $"UV[{index}] is not finite");
            }

            minU = MathF.Min(minU, uv.X);
            maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y);
            maxV = MathF.Max(maxV, uv.Y);
        }

        float scale = sprite.Scale;
        if (!float.IsFinite(scale) || scale <= 0f)
        {
            throw CreateMalformedException(
                sprite,
                "Scale",
                $"expected a finite pixels-per-unit value greater than zero, got {scale}");
        }

        Vector2 pivot = sprite.Pivot;
        if (!float.IsFinite(pivot.X)
            || !float.IsFinite(pivot.Y)
            || pivot.X < 0f
            || pivot.X > 1f
            || pivot.Y < 0f
            || pivot.Y > 1f)
        {
            throw CreateMalformedException(
                sprite,
                "Pivot",
                $"expected a finite normalized pivot in [0,1], got ({pivot.X}, {pivot.Y})");
        }

        if (sprite.Topology != SpriteTopology.Quad)
        {
            throw CreateMalformedException(
                sprite,
                "Topology",
                $"the sprite resolver only accepts Quad, got {sprite.Topology}");
        }

        if (sprite.Indices is not null)
        {
            throw CreateMalformedException(
                sprite,
                "Indices",
                "a Quad must have a null index buffer");
        }

        float worldWidth = ((maxU - minU) * textureWidth) / scale;
        float worldHeight = ((maxV - minV) * textureHeight) / scale;
        return new SpriteQuad(
            WorldWidth: worldWidth,
            WorldHeight: worldHeight,
            SizeX: flipX ? -worldWidth : worldWidth,
            SizeY: flipY ? -worldHeight : worldHeight,
            Pivot: pivot);
    }

    /// <summary>
    /// Creates the fatal renderer exception for a malformed sprite field, naming the field and the
    /// sprite's texture reference (M8 §6.4).
    /// </summary>
    /// <param name="sprite">The malformed sprite asset.</param>
    /// <param name="field">The field name.</param>
    /// <param name="detail">The concrete problem.</param>
    /// <returns>The exception to throw.</returns>
    internal static Bork2dException CreateMalformedException(
        SpriteAsset sprite,
        string field,
        string detail)
    {
        string texture = sprite.Texture.Path is { Length: > 0 } path ? path : "<none>";
        return new Bork2dException(
            $"Sprite asset '{texture}' has a malformed '{field}' field: {detail}.");
    }
}
