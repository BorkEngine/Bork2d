using System.Numerics;

namespace Bork2d.Bake.Svg;

/// <summary>
/// The result of one SVG signed distance field bake: the complete KTX2 payload and the descriptor
/// values that describe it.
/// </summary>
/// <remarks>
/// <see cref="SpriteScale"/> maps the texture's local quad so the visible artboard is exactly one
/// world unit high and keeps its aspect ratio; it is the value a later task assigns to
/// <c>SpriteAsset.Scale</c>. The mip count is available through <see cref="Texture"/>.
/// </remarks>
internal sealed class SvgSdfBakeResult
{
    /// <summary>Initializes a new instance of the <see cref="SvgSdfBakeResult"/> class.</summary>
    /// <param name="texture">The complete KTX2 payload and its format metadata.</param>
    /// <param name="textureWidth">The base-level texture width in pixels.</param>
    /// <param name="textureHeight">The base-level texture height in pixels.</param>
    /// <param name="contentWidth">The artboard content width in pixels, excluding both margins.</param>
    /// <param name="contentHeight">The artboard content height in pixels, excluding both margins.</param>
    /// <param name="pixelsPerUnit">The artboard user-unit to pixel scale.</param>
    /// <param name="spriteScale">The world-space scale that makes the artboard one unit high.</param>
    internal SvgSdfBakeResult(
        BakedTexture texture,
        int textureWidth,
        int textureHeight,
        double contentWidth,
        double contentHeight,
        double pixelsPerUnit,
        Vector2 spriteScale)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        PixelsPerUnit = pixelsPerUnit;
        SpriteScale = spriteScale;
    }

    /// <summary>Gets the complete KTX2 payload and its format metadata.</summary>
    internal BakedTexture Texture { get; }

    /// <summary>Gets the base-level texture width in pixels.</summary>
    internal int TextureWidth { get; }

    /// <summary>Gets the base-level texture height in pixels.</summary>
    internal int TextureHeight { get; }

    /// <summary>Gets the artboard content width in pixels, excluding both margins.</summary>
    internal double ContentWidth { get; }

    /// <summary>Gets the artboard content height in pixels, excluding both margins.</summary>
    internal double ContentHeight { get; }

    /// <summary>Gets the artboard user-unit to pixel scale.</summary>
    internal double PixelsPerUnit { get; }

    /// <summary>Gets the world-space scale that makes the visible artboard one unit high.</summary>
    internal Vector2 SpriteScale { get; }
}
