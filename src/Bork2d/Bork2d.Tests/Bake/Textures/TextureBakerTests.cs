using Bork2d.Bake;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// The public <see cref="TextureBaker"/> helper: format selection, channel packing, the full KTX2
/// payload, determinism, and public-API guards.
/// </summary>
public sealed class TextureBakerTests
{
    [Fact]
    public void BakesRgbaUncompressedWithAFullMipChain()
    {
        byte[] pixels = Solid(2, 2, 10, 20, 30, 200);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            2,
            2,
            TextureChannelType.Rgba,
            TextureCompression.None);

        Assert.Equal(TextureFormat.Rgba8, baked.Format);
        Assert.Equal(2u, baked.MipLevels);

        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(2u, document.Width);
        Assert.Equal(2u, document.Height);
        Assert.Equal(2u, document.LevelCount);
        Assert.Equal(pixels, document.GetLevelBytes(0));
        Assert.Equal([10, 20, 30, 200], document.GetLevelBytes(1));
    }

    [Fact]
    public void RgbForcesOpaqueAlphaInTheStoredPayload()
    {
        byte[] pixels = Solid(2, 2, 10, 20, 30, 0);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            2,
            2,
            TextureChannelType.Rgb,
            TextureCompression.None);

        Assert.Equal(TextureFormat.Rgb8, baked.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());
        for (int level = 0; level < document.Levels.Count; level++)
        {
            byte[] levelBytes = document.GetLevelBytes(level);
            for (int alpha = 3; alpha < levelBytes.Length; alpha += 4)
            {
                Assert.Equal(255, levelBytes[alpha]);
            }
        }

        Assert.Equal([10, 20, 30, 255], document.GetLevelBytes(1));
    }

    [Fact]
    public void RgPacksOnlyTheRequestedChannels()
    {
        // Pixels: (1, 2), (3, 4), (5, 6), (7, 8); the blue and alpha bytes must not be stored.
        byte[] pixels =
        [
            1, 2, 90, 91,
            3, 4, 92, 93,
            5, 6, 94, 95,
            7, 8, 96, 97,
        ];

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            2,
            2,
            TextureChannelType.Rg,
            TextureCompression.None);

        Assert.Equal(TextureFormat.Rg8, baked.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());
        Assert.Equal(16u, document.VkFormat);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], document.GetLevelBytes(0));
        Assert.Equal([4, 5], document.GetLevelBytes(1));
    }

    [Fact]
    public void RPacksOnlyTheRequestedChannel()
    {
        byte[] pixels =
        [
            1, 10, 20, 30,
            3, 11, 21, 31,
            5, 12, 22, 32,
            7, 13, 23, 33,
        ];

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            2,
            2,
            TextureChannelType.R,
            TextureCompression.None);

        Assert.Equal(TextureFormat.R8, baked.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());
        Assert.Equal(9u, document.VkFormat);
        Assert.Equal([1, 3, 5, 7], document.GetLevelBytes(0));
        Assert.Equal([4], document.GetLevelBytes(1));
    }

    [Theory]
    [InlineData(TextureChannelType.Rgba, TextureFormat.CompRgba8, 145u, 16)]
    [InlineData(TextureChannelType.Rgb, TextureFormat.CompRgb8, 131u, 8)]
    [InlineData(TextureChannelType.Rg, TextureFormat.CompRg8, 141u, 16)]
    [InlineData(TextureChannelType.R, TextureFormat.CompR8, 139u, 8)]
    public void AutoSelectsTheMatchingBlockFormat(
        TextureChannelType channelType,
        TextureFormat expectedFormat,
        uint expectedVkFormat,
        int blockBytes)
    {
        BakedTexture baked = TextureBaker.Bake(
            Solid(5, 5, 200, 40, 90, 128),
            5,
            5,
            channelType,
            TextureCompression.Auto);

        Assert.Equal(expectedFormat, baked.Format);
        Assert.Equal(3u, baked.MipLevels);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.ToArray());
        Assert.Equal(expectedVkFormat, document.VkFormat);
        Assert.Equal(3u, document.LevelCount);
        Assert.Equal((ulong)(blockBytes * 4), document.Levels[0].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[1].ByteLength);
        Assert.Equal((ulong)blockBytes, document.Levels[2].ByteLength);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        byte[] pixels = Solid(7, 3, 11, 22, 33, 44);

        BakedTexture first = TextureBaker.Bake(
            pixels,
            7,
            3,
            TextureChannelType.Rgba,
            TextureCompression.None);
        BakedTexture second = TextureBaker.Bake(
            pixels,
            7,
            3,
            TextureChannelType.Rgba,
            TextureCompression.None);

        Assert.Equal(first.Payload.ToArray(), second.Payload.ToArray());
        Assert.Equal(first.Format, second.Format);
        Assert.Equal(first.MipLevels, second.MipLevels);
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        byte[] pixels = Solid(2, 2, 1, 2, 3, 4);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 0, 2, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 0, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentException>(
            () => TextureBaker.Bake(
                pixels.AsSpan(0, pixels.Length - 1),
                2,
                2,
                TextureChannelType.Rgba,
                TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 2, (TextureChannelType)99, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 2, TextureChannelType.Rgba, (TextureCompression)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, int.MaxValue, int.MaxValue, TextureChannelType.Rgba, TextureCompression.None));
    }

    private static byte[] Solid(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int offset = pixel * 4;
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = alpha;
        }

        return pixels;
    }
}
