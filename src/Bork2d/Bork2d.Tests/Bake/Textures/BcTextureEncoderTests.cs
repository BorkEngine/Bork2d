using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// BC1/BC4/BC5/BC7 encoding through <see cref="TextureBaker"/>: level extents, the block-channel
/// routing, and a tolerant decode round-trip with the library decoder.
/// </summary>
public sealed class BcTextureEncoderTests
{
    [Fact]
    public void Bc7RoundTripsRgbaColorAndAlpha()
    {
        BakedTexture baked = Bake(TextureChannelType.Rgba, 200, 40, 90, 128);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());

        ColorRgba32 pixel = DecodeLevel(document, 0, CompressionFormat.Bc7, 5, 5)[0];
        AssertClose(200, pixel.r, 4);
        AssertClose(40, pixel.g, 4);
        AssertClose(90, pixel.b, 4);
        AssertClose(128, pixel.a, 4);
    }

    [Fact]
    public void OpaqueBc1IgnoresInputAlphaAndDecodesAsOpaque()
    {
        BakedTexture baked = Bake(TextureChannelType.Rgb, 200, 40, 90, 0);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());

        ColorRgba32 pixel = DecodeLevel(document, 0, CompressionFormat.Bc1, 5, 5)[0];
        AssertClose(200, pixel.r, 8);
        AssertClose(40, pixel.g, 8);
        AssertClose(90, pixel.b, 8);
        Assert.Equal(255, pixel.a);
    }

    [Fact]
    public void Bc4RoundTripsTheRedChannel()
    {
        BakedTexture baked = Bake(TextureChannelType.R, 37, 210, 90, 255);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());

        var decoder = new BcDecoder();
        decoder.OutputOptions.Bc4Component = ColorComponent.R;
        ColorRgba32 pixel = decoder.DecodeRaw(
            document.GetLevelBytes(0),
            5,
            5,
            CompressionFormat.Bc4)[0];
        AssertClose(37, pixel.r, 4);
    }

    [Fact]
    public void Bc5RoundTripsRedAndGreen()
    {
        BakedTexture baked = Bake(TextureChannelType.Rg, 37, 210, 90, 255);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());

        var decoder = new BcDecoder();
        decoder.OutputOptions.Bc5Component1 = ColorComponent.R;
        decoder.OutputOptions.Bc5Component2 = ColorComponent.G;
        ColorRgba32 pixel = decoder.DecodeRaw(
            document.GetLevelBytes(0),
            5,
            5,
            CompressionFormat.Bc5)[0];
        AssertClose(37, pixel.r, 4);
        AssertClose(210, pixel.g, 4);
    }

    [Theory]
    [InlineData(TextureChannelType.Rgba, 16)]
    [InlineData(TextureChannelType.Rgb, 8)]
    [InlineData(TextureChannelType.Rg, 16)]
    [InlineData(TextureChannelType.R, 8)]
    public void CompressedLevelsUseTheBlockExtent(TextureChannelType channelType, int blockBytes)
    {
        BakedTexture baked = Bake(channelType, 200, 40, 90, 128);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());

        Assert.Equal(3u, document.LevelCount);
        Assert.Equal((ulong)(blockBytes * 4), document.Levels[0].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[1].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[2].ByteLength);
        Assert.All(document.Levels, entry => Assert.Equal(entry.ByteLength, entry.UncompressedByteLength));
    }

    private static BakedTexture Bake(
        TextureChannelType channelType,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        const int width = 5;
        const int height = 5;
        var pixels = new byte[width * height * 4];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int offset = pixel * 4;
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = alpha;
        }

        return TextureBaker.Bake(
            pixels,
            width,
            height,
            channelType,
            TextureCompression.Auto);
    }

    private static ColorRgba32[] DecodeLevel(
        Ktx2TestDocument document,
        int level,
        CompressionFormat format,
        int width,
        int height)
    {
        var decoder = new BcDecoder();
        return decoder.DecodeRaw(document.GetLevelBytes(level), width, height, format);
    }

    private static void AssertClose(byte expected, byte actual, int tolerance)
        => Assert.InRange(actual, Math.Max(0, expected - tolerance), Math.Min(255, expected + tolerance));
}
