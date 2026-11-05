using Bork2d.Bake;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The SVG SDF baker's artboard layout, field validation, KTX2 output, and failure diagnostics.
/// </summary>
public sealed class SvgSdfBakerTests
{
    [Fact]
    public void WideViewBoxSizesTheTextureAndDescriptor()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());

        Assert.Equal(128, result.TextureWidth);
        Assert.Equal(74, result.TextureHeight);
        Assert.Equal(108, result.ContentWidth, 10);
        Assert.Equal(54, result.ContentHeight, 10);
        Assert.Equal(0.54, result.PixelsPerUnit, 10);
        Assert.Equal(128.0 / 54.0, result.SpriteScale.X, 5);
        Assert.Equal(74.0 / 54.0, result.SpriteScale.Y, 5);
        Assert.Equal(TextureFormat.Rgb8, result.Texture.Format);
        Assert.Equal((uint)TextureMipChain.LevelCount(128, 74), result.Texture.MipLevels);

        Ktx2TestDocument document = SvgSdfTestSupport.Document(result);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(128u, document.Width);
        Assert.Equal(74u, document.Height);
        Assert.Equal(8u, document.LevelCount);
        Assert.Equal(128 * 74 * 4, document.GetLevelBytes(0).Length);
    }

    [Fact]
    public void WideViewBoxHasAnInsideRingAndAnOutsideHoleAndBackground()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());
        byte[] baseLevel = SvgSdfTestSupport.Document(result).GetLevelBytes(0);

        (int holeX, int holeY) = SvgSdfTestSupport.TexelForUserPoint(
            100,
            50,
            result.PixelsPerUnit,
            inset: 10,
            result.TextureHeight);
        (int ringX, int ringY) = SvgSdfTestSupport.TexelForUserPoint(
            130,
            50,
            result.PixelsPerUnit,
            inset: 10,
            result.TextureHeight);
        (int outsideX, int outsideY) = SvgSdfTestSupport.TexelForUserPoint(
            5,
            5,
            result.PixelsPerUnit,
            inset: 10,
            result.TextureHeight);

        Assert.True(
            SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, holeX, holeY) < 0.5,
            "The donut hole center must be outside.");
        Assert.True(
            SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, ringX, ringY) > 0.5,
            "The donut ring must be inside.");
        Assert.True(
            SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, outsideX, outsideY) < 0.5,
            "The artboard background must be outside.");
    }

    [Fact]
    public void TallViewBoxFillsTheLongAxisAndPreservesTheArtboardAspect()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options(),
            SvgSdfTestSupport.TallViewBox);

        Assert.Equal(74, result.TextureWidth);
        Assert.Equal(128, result.TextureHeight);
        Assert.Equal(54, result.ContentWidth, 10);
        Assert.Equal(108, result.ContentHeight, 10);
        Assert.Equal(0.54, result.PixelsPerUnit, 10);
        Assert.Equal(74.0 / 108.0, result.SpriteScale.X, 5);
        Assert.Equal(128.0 / 108.0, result.SpriteScale.Y, 5);

        // The visible artboard is exactly one world unit high and keeps its 1:2 aspect.
        Assert.Equal(1.0, result.SpriteScale.Y * result.ContentHeight / result.TextureHeight, 5);
        Assert.Equal(0.5, result.SpriteScale.X * result.ContentWidth / result.TextureWidth, 5);
    }

    [Theory]
    [InlineData(SvgSdfTestSupport.DonutPath)]
    [InlineData(SvgSdfTestSupport.DonutEvenOddPath)]
    public void FillRuleHoleBakesWithAnOutsideHole(string body)
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(body, SvgSdfTestSupport.Options());
        byte[] baseLevel = SvgSdfTestSupport.Document(result).GetLevelBytes(0);

        (int holeX, int holeY) = SvgSdfTestSupport.TexelForUserPoint(
            100,
            50,
            result.PixelsPerUnit,
            inset: 10,
            result.TextureHeight);
        (int ringX, int ringY) = SvgSdfTestSupport.TexelForUserPoint(
            130,
            50,
            result.PixelsPerUnit,
            inset: 10,
            result.TextureHeight);

        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, holeX, holeY) < 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, ringX, ringY) > 0.5);
    }

    [Fact]
    public void RangeAndPaddingReserveExactlyTheInsetMargin()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            "<rect x=\"0\" y=\"0\" width=\"200\" height=\"100\" />",
            SvgSdfTestSupport.Options(resolution: 128, range: 10, padding: 3));

        // inset = 13, usable = 102, scale = 0.51, content = 102x51, texture = 128x77.
        Assert.Equal(128, result.TextureWidth);
        Assert.Equal(77, result.TextureHeight);
        Assert.Equal(102, result.ContentWidth, 10);
        Assert.Equal(51, result.ContentHeight, 10);
        Assert.Equal(0.51, result.PixelsPerUnit, 10);

        byte[] baseLevel = SvgSdfTestSupport.Document(result).GetLevelBytes(0);
        int middleRow = result.TextureHeight / 2;
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 12, middleRow) < 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 16, middleRow) > 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 114, middleRow) > 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 127, middleRow) < 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 64, 0) < 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(baseLevel, result.TextureWidth, 64, result.TextureHeight - 1) < 0.5);
    }

    [Theory]
    [InlineData(0, 8, 2)]
    [InlineData(128, 0, 2)]
    [InlineData(128, 8, -1)]
    [InlineData(128, 57, 7)]
    [InlineData(128, 60, 10)]
    [InlineData(8193, 8, 2)]
    public void InvalidNumericOptionsReportBK9204(int resolution, int range, int padding)
    {
        var options = new SvgSdfBakeOptions(resolution, range, padding, SdfCompression.None);

        SvgDiagnostics diagnostics = SvgSdfTestSupport.BakeFailure(SvgSdfTestSupport.DonutPath, options);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfOptionsInvalid, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void UndefinedCompressionReportsBK9204()
    {
        var options = new SvgSdfBakeOptions(128, 8, 2, (SdfCompression)99);

        SvgDiagnostics diagnostics = SvgSdfTestSupport.BakeFailure(SvgSdfTestSupport.DonutPath, options);

        Assert.Equal(SvgDiagnosticCodes.SdfOptionsInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void ShapeThatCoversTheWholeTextureReportsBK9206()
    {
        SvgDiagnostics diagnostics = SvgSdfTestSupport.BakeFailure(
            "<rect x=\"-100\" y=\"-100\" width=\"300\" height=\"300\" />",
            SvgSdfTestSupport.Options(resolution: 64),
            "0 0 10 10");

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfBakeFailed, error.Code);
        Assert.Contains("inside at every texel", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyArtworkReportsBK9206()
    {
        var artwork = new SvgArtwork(10, 10, []);
        SvgDiagnostics diagnostics = SvgSdfTestSupport.BakeFailure(
            artwork,
            SvgSdfTestSupport.Options(resolution: 64));

        Assert.Equal(SvgDiagnosticCodes.SdfBakeFailed, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void NonFiniteFieldChannelReportsBK9206()
    {
        var diagnostics = new SvgDiagnostics();
        var destination = new byte[4];

        Assert.False(MsdfField.TryConvertPixel(
            [float.NaN, 0, 0],
            x: 1,
            y: 2,
            destination,
            diagnostics));

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfBakeFailed, error.Code);
        Assert.Contains("(1, 2)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteChannelsAreRejectedAndFiniteChannelsAreClampedAndRounded()
    {
        Assert.False(MsdfField.TryConvertChannel(float.NaN, out _));
        Assert.False(MsdfField.TryConvertChannel(float.PositiveInfinity, out _));
        Assert.False(MsdfField.TryConvertChannel(float.NegativeInfinity, out _));

        Assert.True(MsdfField.TryConvertChannel(-1, out byte low));
        Assert.True(MsdfField.TryConvertChannel(2, out byte high));
        Assert.True(MsdfField.TryConvertChannel(0.5f, out byte middle));
        Assert.True(MsdfField.TryConvertChannel(0, out byte zero));
        Assert.True(MsdfField.TryConvertChannel(1, out byte one));

        Assert.Equal(0, low);
        Assert.Equal(255, high);
        Assert.Equal(128, middle);
        Assert.Equal(0, zero);
        Assert.Equal(255, one);
    }

    [Fact]
    public void ContoursOfEverySegmentKindBake()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            "<path d=\"M 40 40 L 100 40 Q 140 40 140 80 C 140 120 100 140 60 120 Z\" />",
            SvgSdfTestSupport.Options());

        Assert.Equal(128, result.TextureWidth);
        Assert.Equal(74, result.TextureHeight);
    }

    [Fact]
    public void SameArtworkAndOptionsProduceTheSamePayload()
    {
        SvgSdfBakeResult first = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());
        SvgSdfBakeResult second = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());

        Assert.Equal(first.Texture.Payload.ToArray(), second.Texture.Payload.ToArray());
        Assert.Equal(first.Texture.Format, second.Texture.Format);
        Assert.Equal(first.Texture.MipLevels, second.Texture.MipLevels);
    }

    [Fact]
    public void UncompressedOutputIsTheSharedMipChain()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());
        Ktx2TestDocument document = SvgSdfTestSupport.Document(result);
        TextureLevel[] expected = TextureMipChain.Generate(
            document.GetLevelBytes(0),
            (int)document.Width,
            (int)document.Height,
            TextureChannelType.Rgb);

        Assert.Equal(TextureFormat.Rgb8, result.Texture.Format);
        Assert.Equal((uint)expected.Length, document.LevelCount);
        for (int level = 0; level < expected.Length; level++)
        {
            Assert.Equal(expected[level].Pixels, document.GetLevelBytes(level));
            Assert.All(
                AlphaValues(expected[level].Pixels),
                value => Assert.Equal(255, value));
        }
    }

    [Fact]
    public void ZeroPaddingStillReservesTheRangeMargin()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options(resolution: 128, range: 4, padding: 0));

        // inset = 4, usable = 120, scale = 0.6, content = 120x60, texture = 128x68.
        Assert.Equal(128, result.TextureWidth);
        Assert.Equal(68, result.TextureHeight);
        Assert.Equal(0.6, result.PixelsPerUnit, 10);
    }

    private static IEnumerable<byte> AlphaValues(byte[] rgba)
    {
        for (int offset = 3; offset < rgba.Length; offset += 4)
        {
            yield return rgba[offset];
        }
    }
}
