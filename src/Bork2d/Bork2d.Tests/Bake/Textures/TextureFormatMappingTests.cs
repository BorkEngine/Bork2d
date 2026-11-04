using Bork2d.Bake;
using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// The sidecar channel-type/compression mapping and the KTX2 format metadata it selects.
/// </summary>
public sealed class TextureFormatMappingTests
{
    public static TheoryData<TextureChannelType, TextureCompression, TextureFormat, uint> Mappings =>
        new()
        {
            { TextureChannelType.Rgba, TextureCompression.None, TextureFormat.Rgba8, 37 },
            { TextureChannelType.Rgb, TextureCompression.None, TextureFormat.Rgb8, 37 },
            { TextureChannelType.Rg, TextureCompression.None, TextureFormat.Rg8, 16 },
            { TextureChannelType.R, TextureCompression.None, TextureFormat.R8, 9 },
            { TextureChannelType.Rgba, TextureCompression.Auto, TextureFormat.CompRgba8, 145 },
            { TextureChannelType.Rgb, TextureCompression.Auto, TextureFormat.CompRgb8, 131 },
            { TextureChannelType.Rg, TextureCompression.Auto, TextureFormat.CompRg8, 141 },
            { TextureChannelType.R, TextureCompression.Auto, TextureFormat.CompR8, 139 },
        };

    [Theory]
    [MemberData(nameof(Mappings))]
    public void MapsChannelTypeAndCompressionToTheDocumentedFormat(
        TextureChannelType channelType,
        TextureCompression compression,
        TextureFormat expectedFormat,
        uint expectedVkFormat)
    {
        Assert.True(TextureFormatMapping.IsDefined(channelType, compression));
        TextureFormat format = TextureFormatMapping.Map(channelType, compression);
        Assert.Equal(expectedFormat, format);

        TextureFormatInfo info = TextureFormatInfo.For(format);
        Assert.Equal(expectedFormat, info.Format);
        Assert.Equal(expectedVkFormat, info.VkFormat);
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8, 1, 1, 4, 1)]
    [InlineData(TextureFormat.Rgb8, 1, 1, 4, 1)]
    [InlineData(TextureFormat.Rg8, 1, 1, 2, 1)]
    [InlineData(TextureFormat.R8, 1, 1, 1, 1)]
    [InlineData(TextureFormat.CompRgba8, 4, 4, 16, 134)]
    [InlineData(TextureFormat.CompRgb8, 4, 4, 8, 128)]
    [InlineData(TextureFormat.CompRg8, 4, 4, 16, 132)]
    [InlineData(TextureFormat.CompR8, 4, 4, 8, 131)]
    public void FormatMetadataMatchesTheVulkanAndDfdTables(
        TextureFormat format,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock,
        byte colorModel)
    {
        TextureFormatInfo info = TextureFormatInfo.For(format);

        Assert.Equal(blockWidth, info.BlockWidth);
        Assert.Equal(blockHeight, info.BlockHeight);
        Assert.Equal(bytesPerBlock, info.BytesPerBlock);
        Assert.Equal(colorModel, info.ColorModel);
        Assert.Equal(blockWidth != 1 || blockHeight != 1, info.IsBlockCompressed);
        Assert.Equal(
            blockWidth == 1 ? 4 : bytesPerBlock,
            info.RequiredAlignment);
    }

    [Fact]
    public void EncodesTheWidthAndHeightOfEveryLevel()
    {
        TextureFormatInfo rgba = TextureFormatInfo.For(TextureFormat.Rgba8);
        Assert.Equal(32, rgba.GetLevelByteLength(4, 2));
        Assert.Equal(4, rgba.GetLevelByteLength(1, 1));

        TextureFormatInfo bc7 = TextureFormatInfo.For(TextureFormat.CompRgba8);
        Assert.Equal(16, bc7.GetLevelByteLength(1, 1));
        Assert.Equal(16, bc7.GetLevelByteLength(4, 4));
        Assert.Equal(64, bc7.GetLevelByteLength(5, 5));

        TextureFormatInfo rg8 = TextureFormatInfo.For(TextureFormat.Rg8);
        Assert.Equal(2, rg8.GetLevelByteLength(1, 1));
        Assert.Equal(18, rg8.GetLevelByteLength(3, 3));
    }

    [Fact]
    public void RejectsUndefinedEnumValues()
    {
        Assert.False(TextureFormatMapping.IsDefined((TextureChannelType)99, TextureCompression.None));
        Assert.False(TextureFormatMapping.IsDefined(TextureChannelType.Rgba, (TextureCompression)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureFormatMapping.Map((TextureChannelType)99, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureFormatInfo.For((TextureFormat)99));
    }

    [Fact]
    public void SerializedTextureFormatValuesAreStable()
    {
        Assert.Equal(0, (int)TextureFormat.Rgba8);
        Assert.Equal(1, (int)TextureFormat.Rgb8);
        Assert.Equal(2, (int)TextureFormat.Rg8);
        Assert.Equal(3, (int)TextureFormat.R8);
        Assert.Equal(4, (int)TextureFormat.CompRgba8);
        Assert.Equal(5, (int)TextureFormat.CompRgb8);
        Assert.Equal(6, (int)TextureFormat.CompRg8);
        Assert.Equal(7, (int)TextureFormat.CompR8);
    }
}
