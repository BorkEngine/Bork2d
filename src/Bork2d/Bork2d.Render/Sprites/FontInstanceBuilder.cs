using System.Numerics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Sprites;

/// <summary>
/// Builds the 176-byte <see cref="SpriteInstance"/> record for one laid-out font glyph quad, using the
/// sprite instance ABI and the sprite vertex stage (M5 font render path).
/// </summary>
/// <remarks>
/// <para>
/// A glyph shares the per-frame sprite instance buffer and vertex stage: there is no glyph-specific
/// buffer, descriptor binding, or vertex shader. The record uses the quad's world top-left corner as
/// the top-left-origin pivot <c>(0, 0)</c>, so the sprite vertex stage expands the four corners over
/// the glyph quad exactly as it does for a sprite. UVs are assigned to the same corners: top-left,
/// top-right, bottom-left, and bottom-right in the triangle-strip order the sprite stage uses.
/// </para>
/// <para>
/// The builder is pure and allocation-free. The caller has already validated the font, resolved its
/// shader entry and atlas page, and seeded the four instance-uniform lanes (the SDF default's
/// declared defaults for the built-in shader, or the font's frozen custom-shader lanes).
/// </para>
/// </remarks>
internal static class FontInstanceBuilder
{
    /// <summary>Builds one glyph instance record.</summary>
    /// <param name="font">The captured font values, including its composed world matrix and color.</param>
    /// <param name="quad">The laid-out glyph quad in font-local space.</param>
    /// <param name="atlasIndex">The bindless atlas page index; <c>0</c> is the placeholder.</param>
    /// <param name="params0">The first four float lanes of the instance uniform block.</param>
    /// <param name="params1">The second four float lanes of the instance uniform block.</param>
    /// <param name="params2">The third four float lanes of the instance uniform block.</param>
    /// <param name="params3">The fourth four float lanes of the instance uniform block.</param>
    /// <returns>The instance record to write into the shared mapped buffer.</returns>
    internal static SpriteInstance Create(
        in FontSnapshot font,
        in FontGlyphQuad quad,
        uint atlasIndex,
        Vector4 params0,
        Vector4 params1,
        Vector4 params2,
        Vector4 params3)
    {
        Matrix3x2 world = font.WorldMatrix;
        Vector2 worldTopLeft = Vector2.Transform(
            new Vector2(quad.Min.X, quad.Min.Y + quad.Size.Y),
            world);
        return new SpriteInstance
        {
            Transform0 = new Vector4(world.M11, world.M12, world.M21, world.M22),
            Transform1 = new Vector4(worldTopLeft.X, worldTopLeft.Y, quad.Size.X, quad.Size.Y),
            Uv0 = new Vector4(quad.Uv.X, quad.Uv.Y, quad.Uv.Z, quad.Uv.Y),
            Uv1 = new Vector4(quad.Uv.Z, quad.Uv.W, quad.Uv.X, quad.Uv.W),
            Tint = font.Color,
            Misc = new Vector4(0f, 0f, atlasIndex, 0f),
            ExtraTextureIndices = default,
            InstanceParams0 = params0,
            InstanceParams1 = params1,
            InstanceParams2 = params2,
            InstanceParams3 = params3,
        };
    }
}
