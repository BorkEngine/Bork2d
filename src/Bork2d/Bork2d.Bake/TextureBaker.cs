using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake;

/// <summary>
/// Builds a complete linear KTX2 texture payload from straight-alpha RGBA8 pixels.
/// </summary>
/// <remarks>
/// <para>
/// The helper is the one KTX2 producer for the bake: <see cref="Processors.TextureProcessor"/> calls it
/// for decoded image sources, and a custom processor (for example a generated texture) calls it
/// directly so it never hand-writes KTX2 headers or block data. It generates the full mip chain,
/// applies the channel type's semantics, block-encodes every level when compression is requested, and
/// returns the payload together with the descriptor format and mip count.
/// </para>
/// <para>
/// Input pixels are straight-alpha RGBA8, row-major with a top-left origin. All data is linear: there
/// is no gamma conversion, premultiplication, flipping, or resampling beyond the mip box filter.
/// <see cref="TextureChannelType.Rgb"/> forces alpha to <c>255</c>; <see cref="TextureChannelType.Rg"/>
/// and <see cref="TextureChannelType.R"/> keep only the channels they name.
/// </para>
/// </remarks>
public static class TextureBaker
{
    /// <summary>Bakes one texture into a complete KTX2 payload.</summary>
    /// <param name="rgba8Pixels">
    /// The base-level straight-alpha RGBA8 pixels, exactly <c>width × height × 4</c> bytes.
    /// </param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <returns>
    /// The KTX2 payload and the descriptor format and mip count that describe it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, the pixel count overflows, or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    public static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression)
    {
        if (!TextureFormatMapping.IsDefined(channelType, compression))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        if ((long)width * height > int.MaxValue / 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The RGBA8 pixel count does not fit in one array.");
        }

        long expectedLength = (long)width * height * 4;
        if (rgba8Pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"The RGBA8 pixel buffer is {rgba8Pixels.Length} bytes; expected {expectedLength} "
                    + "(width × height × 4).",
                nameof(rgba8Pixels));
        }

        TextureFormat format = TextureFormatMapping.Map(channelType, compression);
        TextureFormatInfo formatInfo = TextureFormatInfo.For(format);
        TextureLevel[] levels = TextureMipChain.Generate(rgba8Pixels, width, height, channelType);
        var storedLevels = new byte[levels.Length][];
        for (int level = 0; level < levels.Length; level++)
        {
            storedLevels[level] = formatInfo.IsBlockCompressed
                ? BcTextureEncoder.Encode(levels[level], format)
                : PackUncompressed(levels[level], format);
        }

        byte[] payload = Ktx2Writer.Write(formatInfo, width, height, storedLevels);
        return new BakedTexture(payload, format, (uint)levels.Length);
    }

    private static byte[] PackUncompressed(TextureLevel level, TextureFormat format)
    {
        switch (format)
        {
            case TextureFormat.Rgba8:
            case TextureFormat.Rgb8:
                return level.Pixels;

            case TextureFormat.Rg8:
                var rg = new byte[level.Width * level.Height * 2];
                for (int pixel = 0; pixel < level.Width * level.Height; pixel++)
                {
                    int source = pixel * 4;
                    int destination = pixel * 2;
                    rg[destination] = level.Pixels[source];
                    rg[destination + 1] = level.Pixels[source + 1];
                }

                return rg;

            case TextureFormat.R8:
                var r = new byte[level.Width * level.Height];
                for (int pixel = 0; pixel < level.Width * level.Height; pixel++)
                {
                    r[pixel] = level.Pixels[pixel * 4];
                }

                return r;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "The format is not an uncompressed format.");
        }
    }
}
