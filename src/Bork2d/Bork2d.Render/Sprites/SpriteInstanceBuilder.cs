using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Sprites;

/// <summary>
/// Builds the 96-byte <see cref="SpriteInstance"/> record from a resolved snapshot and quad
/// (M8 §6.4/§6.6).
/// </summary>
/// <remarks>
/// The builder is pure and allocation-free. It assumes the caller already validated the sprite and
/// resolved its quad; <c>flags</c> stays zero because flips are encoded as the sign of
/// <c>sizeX</c>/<c>sizeY</c>.
/// </remarks>
internal static class SpriteInstanceBuilder
{
    /// <summary>Builds one instance record.</summary>
    /// <param name="sprite">The captured sprite values.</param>
    /// <param name="quad">The resolved world-unit quad.</param>
    /// <param name="textureIndex">The bindless page index; <c>0</c> is the placeholder.</param>
    /// <returns>The instance record to write into the mapped buffer.</returns>
    internal static SpriteInstance Create(
        in SpriteSnapshot sprite,
        in SpriteQuad quad,
        uint textureIndex)
    {
        SpriteRenderData data = sprite.SpriteData
            ?? throw new ArgumentException("A null sprite has no instance.", nameof(sprite));
        Vector2[] uvs = data.Uvs
            ?? throw new ArgumentException("The sprite has no UVs.", nameof(sprite));
        if (uvs.Length != 4)
        {
            throw new ArgumentException("The sprite must have exactly four UVs.", nameof(sprite));
        }

        Matrix3x2 world = sprite.WorldMatrix;
        return new SpriteInstance
        {
            Transform0 = new Vector4(world.M11, world.M12, world.M21, world.M22),
            Transform1 = new Vector4(world.M31, world.M32, quad.SizeX, quad.SizeY),
            Uv0 = new Vector4(uvs[0].X, uvs[0].Y, uvs[1].X, uvs[1].Y),
            Uv1 = new Vector4(uvs[2].X, uvs[2].Y, uvs[3].X, uvs[3].Y),
            Tint = sprite.Tint,
            Misc = new Vector4(quad.Pivot.X, quad.Pivot.Y, textureIndex, 0f),
        };
    }
}
