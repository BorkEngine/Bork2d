using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Textures;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Deterministic tests for the runtime format table and the sampled-format support helper: concrete
/// Vulkan formats, KTX2 header values, block/level sizes, alignments, and the fatal unsupported-format
/// message. No Vulkan call or GPU is involved.
/// </summary>
public sealed class RenderTextureFormatTests
{
    [Theory]
    [InlineData(TextureFormat.Rgba8, 37u, Format.R8G8B8A8Unorm, 1, 1, 4)]
    [InlineData(TextureFormat.Rgb8, 37u, Format.R8G8B8A8Unorm, 1, 1, 4)]
    [InlineData(TextureFormat.Rg8, 16u, Format.R8G8Unorm, 1, 1, 2)]
    [InlineData(TextureFormat.R8, 9u, Format.R8Unorm, 1, 1, 1)]
    [InlineData(TextureFormat.CompRgba8, 145u, Format.BC7UnormBlock, 4, 4, 16)]
    [InlineData(TextureFormat.CompRgb8, 131u, Format.BC1RgbUnormBlock, 4, 4, 8)]
    [InlineData(TextureFormat.CompRg8, 141u, Format.BC5UnormBlock, 4, 4, 16)]
    [InlineData(TextureFormat.CompR8, 139u, Format.BC4UnormBlock, 4, 4, 8)]
    public void MapsEveryFormatToItsKtx2AndVulkanFormats(
        TextureFormat format,
        uint expectedVkFormat,
        Format expectedVulkanFormat,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock)
    {
        RenderTextureFormat info = Assert.IsType<RenderTextureFormat>(
            RenderTextureFormat.TryGet(format));

        Assert.Equal(format, info.Format);
        Assert.Equal(expectedVkFormat, info.VkFormat);
        Assert.Equal(expectedVulkanFormat, info.VulkanFormat);
        Assert.Equal(blockWidth, info.BlockWidth);
        Assert.Equal(blockHeight, info.BlockHeight);
        Assert.Equal(bytesPerBlock, info.BytesPerBlock);
        Assert.Equal(blockWidth != 1 || blockHeight != 1, info.IsBlockCompressed);
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8, 4)]
    [InlineData(TextureFormat.Rg8, 4)]
    [InlineData(TextureFormat.R8, 4)]
    [InlineData(TextureFormat.CompRgba8, 16)]
    [InlineData(TextureFormat.CompRgb8, 8)]
    [InlineData(TextureFormat.CompRg8, 16)]
    [InlineData(TextureFormat.CompR8, 8)]
    public void RequiredAlignmentIsTheLeastCommonMultipleWithFour(
        TextureFormat format,
        int expectedAlignment)
    {
        RenderTextureFormat info = RenderTextureFormat.Get(format);

        Assert.Equal(expectedAlignment, info.RequiredAlignment);
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8, 4, 2, 32)]
    [InlineData(TextureFormat.Rgb8, 1, 1, 4)]
    [InlineData(TextureFormat.Rg8, 3, 2, 12)]
    [InlineData(TextureFormat.R8, 7, 1, 7)]
    [InlineData(TextureFormat.CompRgba8, 4, 4, 16)]
    [InlineData(TextureFormat.CompRgba8, 5, 5, 64)]
    [InlineData(TextureFormat.CompRgb8, 5, 1, 16)]
    [InlineData(TextureFormat.CompRg8, 3, 3, 16)]
    [InlineData(TextureFormat.CompR8, 8, 8, 32)]
    public void ComputesStoredLevelLengths(
        TextureFormat format,
        int width,
        int height,
        int expectedLength)
    {
        Assert.Equal(expectedLength, RenderTextureFormat.Get(format).GetLevelByteLength(width, height));
    }

    [Fact]
    public void UndefinedFormatsHaveNoMappingAndProduceAFatalNamedException()
    {
        Assert.Null(RenderTextureFormat.TryGet((TextureFormat)99));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => RenderTextureFormat.Get((TextureFormat)99));

        Assert.Contains("99", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textureCompressionBC", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SamplingRequiresTheSampledImageOptimalTilingFeature()
    {
        var supported = new FormatProperties
        {
            OptimalTilingFeatures = FormatFeatureFlags.SampledImageBit,
        };
        var unsupported = new FormatProperties
        {
            OptimalTilingFeatures = FormatFeatureFlags.TransferDstBit,
        };

        Assert.True(RenderTextureFormat.SupportsSampling(supported));
        Assert.False(RenderTextureFormat.SupportsSampling(unsupported));
    }

    [Fact]
    public void UnsupportedFormatExceptionNamesTheFormatAndTheDesktopRequirement()
    {
        RenderTextureFormat info = RenderTextureFormat.Get(TextureFormat.CompRgba8);

        Bork2dException exception = RenderTextureFormat.CreateUnsupportedFormatException(info);

        Assert.Contains("CompRgba8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textureCompressionBC", exception.Message, StringComparison.Ordinal);
        Assert.Contains("desktop Vulkan 1.3", exception.Message, StringComparison.Ordinal);
    }
}
