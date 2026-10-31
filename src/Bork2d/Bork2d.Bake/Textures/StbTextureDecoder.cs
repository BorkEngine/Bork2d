using StbImageSharp;

namespace Bork2d.Bake.Textures;

/// <summary>
/// The StbImageSharp decoder implementation (D-M4-05): PNG, JPG/JPEG, BMP, and TGA sources
/// normalized to straight-alpha RGBA8.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImageResult.FromStream(Stream, ColorComponents)"/> is asked for
/// <see cref="ColorComponents.RedGreenBlueAlpha"/>, so the codec normalizes palette, grayscale, and
/// JPG sources and supplies alpha 255 where the source has none. No premultiplication, gamma, ICC,
/// flipping, or resampling is performed.
/// </para>
/// <para>
/// StbImageSharp reports corrupt or unsupported input by throwing <see cref="InvalidOperationException"/>;
/// <see cref="TextureSourceReader"/> converts every decoder failure into a BK4xxx diagnostic naming the
/// source.
/// </para>
/// </remarks>
internal sealed class StbTextureDecoder : ITextureDecoder
{
    private StbTextureDecoder()
    {
    }

    /// <summary>Gets the shared stateless decoder.</summary>
    internal static StbTextureDecoder Instance { get; } = new();

    /// <inheritdoc/>
    DecodedImage ITextureDecoder.Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new DecodedImage(image.Width, image.Height, image.Data);
    }
}
