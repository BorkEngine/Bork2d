using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Deterministic tests for the generated placeholder page (M8 §6.5): an 8x8 magenta/black
/// checkerboard, nearest filter, clamp wrap, one mip, index 0, with no payload asset.
/// </summary>
public sealed class TexturePlaceholderTests
{
    [Fact]
    public void PixelsAreAnEightByEightMagentaBlackCheckerboard()
    {
        byte[] pixels = TexturePlaceholder.CreatePixels();

        Assert.Equal(TexturePlaceholder.Size * TexturePlaceholder.Size * 4, pixels.Length);

        for (var y = 0; y < TexturePlaceholder.Size; y++)
        {
            for (var x = 0; x < TexturePlaceholder.Size; x++)
            {
                int offset = ((y * TexturePlaceholder.Size) + x) * 4;
                bool magenta = ((x + y) & 1) == 0;
                Assert.Equal(magenta ? (byte)255 : (byte)0, pixels[offset]);
                Assert.Equal((byte)0, pixels[offset + 1]);
                Assert.Equal(magenta ? (byte)255 : (byte)0, pixels[offset + 2]);
                Assert.Equal((byte)255, pixels[offset + 3]);
            }
        }

        // The top-left texel is magenta, so the pattern is visually obvious as a placeholder.
        Assert.Equal((byte)255, pixels[0]);
        Assert.Equal((byte)255, pixels[2]);
    }

    [Fact]
    public void PixelDataIsFreshPerCall()
    {
        byte[] first = TexturePlaceholder.CreatePixels();
        byte[] second = TexturePlaceholder.CreatePixels();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DescriptorIsRgba8NearestClampWithNoPayload()
    {
        TextureDescriptor descriptor = TexturePlaceholder.CreateDescriptor();

        Assert.Equal((uint)TexturePlaceholder.Size, descriptor.Width);
        Assert.Equal((uint)TexturePlaceholder.Size, descriptor.Height);
        Assert.Equal(TextureFormat.Rgba8, descriptor.Format);
        Assert.Equal(TextureFilter.Nearest, descriptor.Filter);
        Assert.Equal(TextureWrap.Clamp, descriptor.Wrap);
        Assert.False(descriptor.Payload.HasValue);
    }

    [Fact]
    public void PlaceholderIsBindlessIndexZero()
    {
        Assert.Equal(0, TexturePageCapacity.PlaceholderIndex);
    }
}
