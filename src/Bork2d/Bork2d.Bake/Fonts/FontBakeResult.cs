using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// The result of one font atlas bake: the public font DTO and the complete KTX2 atlas payload.
/// </summary>
/// <remarks>
/// <see cref="Font"/> already references the atlas asset path, so a processor only needs to add the
/// font, its descriptor, and <see cref="Texture"/>'s payload to the output sink.
/// </remarks>
internal sealed class FontBakeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FontBakeResult"/> class.
    /// </summary>
    /// <param name="font">The baked font asset.</param>
    /// <param name="texture">The complete KTX2 atlas payload and its descriptor values.</param>
    /// <param name="resolution">The square atlas side length in pixels.</param>
    internal FontBakeResult(FontAsset font, BakedTexture texture, int resolution)
    {
        Font = font;
        Texture = texture;
        Resolution = resolution;
    }

    /// <summary>Gets the baked font asset, including its atlas reference.</summary>
    internal FontAsset Font { get; }

    /// <summary>Gets the complete KTX2 atlas payload.</summary>
    internal BakedTexture Texture { get; }

    /// <summary>Gets the square atlas side length in pixels.</summary>
    internal int Resolution { get; }
}
