using Bork2d.Bake;
using Bork2d.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// Deterministic mip generation: the box filter's expected values, channel semantics, odd-dimension
/// coverage, and the full chain down to one pixel.
/// </summary>
public sealed class TextureMipChainTests
{
    [Fact]
    public void BoxFilterAveragesAllFourChannels()
    {
        // 4x4, R = x * 10, G = y * 20, B = 100, A = 200.
        TextureLevel[] levels = Generate(
            TextureChannelType.Rgba,
            4,
            4,
            (x, y) => ((byte)(x * 10), (byte)(y * 20), 100, 200));

        Assert.Equal(3, levels.Length);
        AssertLevel(levels[1], 2, 2);
        AssertPixel(levels[1], 0, 0, 5, 10, 100, 200);
        AssertPixel(levels[1], 1, 0, 25, 10, 100, 200);
        AssertPixel(levels[1], 0, 1, 5, 50, 100, 200);
        AssertPixel(levels[1], 1, 1, 25, 50, 100, 200);

        AssertLevel(levels[2], 1, 1);
        AssertPixel(levels[2], 0, 0, 15, 30, 100, 200);
    }

    [Fact]
    public void RgbForcesOpaqueAlphaAtEveryLevel()
    {
        TextureLevel[] levels = Generate(
            TextureChannelType.Rgb,
            2,
            2,
            (x, y) => ((byte)(x * 10), (byte)(y * 10), 40, (byte)(x + y)));

        Assert.All(levels, level => Assert.All(AlphaValues(level), value => Assert.Equal(255, value)));
        AssertPixel(levels[0], 0, 0, 0, 0, 40, 255);
        AssertPixel(levels[1], 0, 0, 5, 5, 40, 255);
    }

    [Fact]
    public void RgbaAveragesAlpha()
    {
        TextureLevel[] levels = Generate(
            TextureChannelType.Rgba,
            2,
            2,
            (x, y) => (10, 20, 30, (byte)(x * 255)));

        // Alpha values 0, 255, 0, 255 average to 128 with half-up rounding.
        AssertPixel(levels[1], 0, 0, 10, 20, 30, 128);
    }

    [Fact]
    public void RgPreservesRedAndGreenForEachLevel()
    {
        TextureLevel[] levels = Generate(
            TextureChannelType.Rg,
            2,
            2,
            (x, y) => ((byte)(x * 100), (byte)(y * 50), 7, 9));

        AssertPixel(levels[1], 0, 0, 50, 25, 7, 9);
    }

    [Fact]
    public void RPreservesRedForEachLevel()
    {
        TextureLevel[] levels = Generate(
            TextureChannelType.R,
            2,
            2,
            (x, y) => ((byte)(x * 100 + 10), 1, 2, 3));

        AssertPixel(levels[1], 0, 0, 60, 1, 2, 3);
    }

    [Fact]
    public void BoxFilterCoversEveryPixelOfAnOddDimension()
    {
        // A 3x3 level reduces to one pixel; every source pixel contributes to the average.
        TextureLevel[] levels = Generate(
            TextureChannelType.Rgba,
            3,
            3,
            (x, y) => ((byte)(x * 30), (byte)(y * 30), 15, 200));

        Assert.Equal(1, levels[1].Width);
        Assert.Equal(1, levels[1].Height);
        AssertPixel(levels[1], 0, 0, 30, 30, 15, 200);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(4, 2, 3)]
    [InlineData(5, 3, 3)]
    [InlineData(8, 1, 4)]
    [InlineData(64, 32, 7)]
    public void ChainEndsAtOneByOne(int width, int height, int expectedLevels)
    {
        TextureLevel[] levels = Generate(
            TextureChannelType.Rgba,
            width,
            height,
            (_, _) => (1, 2, 3, 4));

        Assert.Equal(expectedLevels, levels.Length);
        Assert.Equal(1, levels[^1].Width);
        Assert.Equal(1, levels[^1].Height);
        for (int level = 0; level < levels.Length; level++)
        {
            Assert.Equal(TextureMipChain.LevelWidth(width, level), levels[level].Width);
            Assert.Equal(TextureMipChain.LevelHeight(height, level), levels[level].Height);
        }
    }

    [Fact]
    public void RejectsAMismatchedPixelBuffer()
    {
        Assert.Throws<ArgumentException>(
            () => TextureMipChain.Generate([1, 2, 3], 2, 2, TextureChannelType.Rgba));
    }

    [Fact]
    public void RejectsNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureMipChain.Generate([], 0, 2, TextureChannelType.Rgba));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureMipChain.Generate([], 2, 0, TextureChannelType.Rgba));
    }

    private static TextureLevel[] Generate(
        TextureChannelType channelType,
        int width,
        int height,
        Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b, byte a) = pixel(x, y);
                int offset = ((y * width) + x) * 4;
                rgba[offset] = r;
                rgba[offset + 1] = g;
                rgba[offset + 2] = b;
                rgba[offset + 3] = a;
            }
        }

        return TextureMipChain.Generate(rgba, width, height, channelType);
    }

    private static IEnumerable<byte> AlphaValues(TextureLevel level)
    {
        for (int index = 3; index < level.Pixels.Length; index += 4)
        {
            yield return level.Pixels[index];
        }
    }

    private static void AssertLevel(TextureLevel level, int width, int height)
    {
        Assert.Equal(width, level.Width);
        Assert.Equal(height, level.Height);
        Assert.Equal(width * height * 4, level.Pixels.Length);
    }

    private static void AssertPixel(
        TextureLevel level,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = ((y * level.Width) + x) * 4;
        Assert.Equal(red, level.Pixels[offset]);
        Assert.Equal(green, level.Pixels[offset + 1]);
        Assert.Equal(blue, level.Pixels[offset + 2]);
        Assert.Equal(alpha, level.Pixels[offset + 3]);
    }
}
