using Bork2d.Bake;
using Bork2d.Engine.Assets;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Builds real KTX2 payloads and matching descriptors with the bake tooling, so runtime and GPU tests
/// never hand-write container bytes and no binary fixture is committed.
/// </summary>
/// <remarks>
/// Tests may call this because <c>Bork2d.Tests</c> references <c>Bork2d.Bake</c>. The production
/// renderer does not: it only parses the resulting KTX2 payload.
/// </remarks>
internal static class Ktx2TextureFactory
{
    /// <summary>Creates a solid straight-alpha RGBA8 base level.</summary>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <param name="r">The red byte.</param>
    /// <param name="g">The green byte.</param>
    /// <param name="b">The blue byte.</param>
    /// <param name="a">The alpha byte.</param>
    /// <returns>The row-major RGBA8 pixels.</returns>
    internal static byte[] SolidPixels(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = r;
            pixels[offset + 1] = g;
            pixels[offset + 2] = b;
            pixels[offset + 3] = a;
        }

        return pixels;
    }

    /// <summary>Creates a one-pixel alternating red/green checkerboard base level.</summary>
    /// <param name="resolution">The square resolution in pixels.</param>
    /// <returns>The row-major RGBA8 pixels; even cells are red, odd cells are green.</returns>
    internal static byte[] RedGreenCheckerboard(int resolution)
    {
        var pixels = new byte[checked(resolution * resolution * 4)];
        for (var y = 0; y < resolution; y++)
        {
            for (var x = 0; x < resolution; x++)
            {
                bool red = ((x + y) & 1) == 0;
                int offset = ((y * resolution) + x) * 4;
                pixels[offset] = red ? (byte)255 : (byte)0;
                pixels[offset + 1] = red ? (byte)0 : (byte)255;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Bakes a solid texture with a full mip chain.</summary>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <param name="r">The red byte.</param>
    /// <param name="g">The green byte.</param>
    /// <param name="b">The blue byte.</param>
    /// <param name="a">The alpha byte.</param>
    /// <returns>The baked KTX2 payload and descriptor values.</returns>
    internal static BakedTexture BakeSolid(
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        byte r = 255,
        byte g = 0,
        byte b = 255,
        byte a = 255)
        => TextureBaker.Bake(
            SolidPixels(width, height, r, g, b, a),
            width,
            height,
            channelType,
            compression);

    /// <summary>Gets the channel type and compression mode that bake a concrete format.</summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns>The matching bake inputs.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined format.</exception>
    internal static (TextureChannelType ChannelType, TextureCompression Compression) BakeInputs(
        TextureFormat format)
        => format switch
        {
            TextureFormat.Rgba8 => (TextureChannelType.Rgba, TextureCompression.None),
            TextureFormat.Rgb8 => (TextureChannelType.Rgb, TextureCompression.None),
            TextureFormat.Rg8 => (TextureChannelType.Rg, TextureCompression.None),
            TextureFormat.R8 => (TextureChannelType.R, TextureCompression.None),
            TextureFormat.CompRgba8 => (TextureChannelType.Rgba, TextureCompression.Auto),
            TextureFormat.CompRgb8 => (TextureChannelType.Rgb, TextureCompression.Auto),
            TextureFormat.CompRg8 => (TextureChannelType.Rg, TextureCompression.Auto),
            TextureFormat.CompR8 => (TextureChannelType.R, TextureCompression.Auto),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format has no bake input mapping."),
        };

    /// <summary>Bakes a solid texture in the requested concrete format.</summary>
    /// <param name="format">The target format.</param>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <param name="r">The red byte.</param>
    /// <param name="g">The green byte.</param>
    /// <param name="b">The blue byte.</param>
    /// <param name="a">The alpha byte.</param>
    /// <returns>The baked KTX2 payload and descriptor values.</returns>
    internal static BakedTexture BakeSolid(
        TextureFormat format,
        int width,
        int height,
        byte r = 255,
        byte g = 0,
        byte b = 255,
        byte a = 255)
    {
        (TextureChannelType channelType, TextureCompression compression) = BakeInputs(format);
        return BakeSolid(width, height, channelType, compression, r, g, b, a);
    }

    /// <summary>Creates a descriptor that matches a baked texture.</summary>
    /// <param name="payloadPath">The canonical payload path.</param>
    /// <param name="baked">The baked texture.</param>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <param name="filter">The sampler filter.</param>
    /// <param name="wrap">The sampler wrap.</param>
    /// <returns>The matching descriptor.</returns>
    internal static TextureDescriptor Descriptor(
        string payloadPath,
        BakedTexture baked,
        uint width,
        uint height,
        TextureFilter filter = TextureFilter.Nearest,
        TextureWrap wrap = TextureWrap.Clamp)
        => new()
        {
            Payload = payloadPath,
            Width = width,
            Height = height,
            Format = baked.Format,
            MipLevels = baked.MipLevels,
            Filter = filter,
            Wrap = wrap,
        };
}
