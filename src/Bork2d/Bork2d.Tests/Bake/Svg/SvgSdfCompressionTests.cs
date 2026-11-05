using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Bork2d.Bake;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The opt-in BC7 output: container shape, base-level fidelity, decoded sign preservation on every
/// mip, determinism, and the <c>BK9207</c> failure branches.
/// </summary>
public sealed class SvgSdfCompressionTests
{
    private const double MaxLevelPixelError = 1.25;
    private const double MaxRmsLevelPixelError = 0.10;

    [Fact]
    public void Bc7DonutUsesTheBc7ContainerAndPassesTheGates()
    {
        SvgSdfBakeResult uncompressed = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());
        SvgSdfBakeResult compressed = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options(compression: SdfCompression.Bc7));

        Assert.Equal(TextureFormat.CompRgba8, compressed.Texture.Format);
        Assert.Equal(uncompressed.SpriteScale, compressed.SpriteScale);

        Ktx2TestDocument document = SvgSdfTestSupport.Document(compressed);
        Assert.Equal(145u, document.VkFormat);
        Assert.Equal((uint)TextureMipChain.LevelCount(128, 74), document.LevelCount);
        for (int level = 0; level < document.LevelCount; level++)
        {
            int width = TextureMipChain.LevelWidth(128, level);
            int height = TextureMipChain.LevelHeight(74, level);
            Assert.Equal(BlockLength(width, height), document.GetLevelBytes(level).Length);
        }

        Ktx2TestDocument source = SvgSdfTestSupport.Document(uncompressed);
        var sourceLevel = new TextureLevel(128, 74, source.GetLevelBytes(0));
        ColorRgba32[] decodedLevel = new BcDecoder().DecodeRaw(
            document.GetLevelBytes(0),
            128,
            74,
            CompressionFormat.Bc7);
        (double maximum, double rms) = MedianErrors(sourceLevel, decodedLevel, range: 8);
        Assert.True(
            maximum <= MaxLevelPixelError,
            $"The BC7 base-level maximum error was {maximum} level pixels.");
        Assert.True(
            rms <= MaxRmsLevelPixelError,
            $"The BC7 base-level RMS error was {rms} level pixels.");

        AssertDecodedSigns(document, level: 0, holeX: 64, holeY: 36, ringX: 80, ringY: 36);
        AssertDecodedSigns(document, level: 1, holeX: 32, holeY: 18, ringX: 40, ringY: 18);
        AssertDecodedSigns(document, level: 2, holeX: 16, holeY: 8, ringX: 20, ringY: 9);
    }

    [Fact]
    public void Bc7ThinStarBakesAndPassesTheBaseLevelFidelityGate()
    {
        SvgSdfBakeResult uncompressed = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.StarPath,
            SvgSdfTestSupport.Options());
        SvgSdfBakeResult compressed = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.StarPath,
            SvgSdfTestSupport.Options(compression: SdfCompression.Bc7));

        Assert.Equal(TextureFormat.CompRgba8, compressed.Texture.Format);
        Ktx2TestDocument document = SvgSdfTestSupport.Document(compressed);
        Ktx2TestDocument source = SvgSdfTestSupport.Document(uncompressed);
        var sourceLevel = new TextureLevel(128, 74, source.GetLevelBytes(0));
        ColorRgba32[] decodedLevel = new BcDecoder().DecodeRaw(
            document.GetLevelBytes(0),
            128,
            74,
            CompressionFormat.Bc7);
        (double maximum, double rms) = MedianErrors(sourceLevel, decodedLevel, range: 8);
        Assert.True(maximum <= MaxLevelPixelError);
        Assert.True(rms <= MaxRmsLevelPixelError);
    }

    [Fact]
    public void Bc7IsDeterministic()
    {
        SvgSdfBakeResult first = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options(compression: SdfCompression.Bc7));
        SvgSdfBakeResult second = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options(compression: SdfCompression.Bc7));

        Assert.Equal(first.Texture.Payload.ToArray(), second.Texture.Payload.ToArray());
    }

    [Fact]
    public void Bc7BaseLevelFidelityFailureReportsBK9207()
    {
        MsdfField field = CreateDonutField();
        TextureLevel[] levels = TextureMipChain.Generate(
            field.Pixels,
            128,
            74,
            TextureChannelType.Rgb);
        var decoded = new byte[levels[0].Pixels.Length];
        Array.Fill(decoded, (byte)255);

        var diagnostics = new SvgDiagnostics();
        Assert.False(SdfBc7Validator.ValidateLevel(field, levels[0], decoded, 0, diagnostics));

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfCompressionFailed, error.Code);
        Assert.Contains("mip level 0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bc7DecodedSignFailureReportsBK9207()
    {
        MsdfField field = CreateDonutField();
        TextureLevel[] levels = TextureMipChain.Generate(
            field.Pixels,
            128,
            74,
            TextureChannelType.Rgb);
        byte[] inverted = [.. levels[1].Pixels];
        for (int offset = 0; offset < inverted.Length; offset += 4)
        {
            inverted[offset] = (byte)(255 - inverted[offset]);
            inverted[offset + 1] = (byte)(255 - inverted[offset + 1]);
            inverted[offset + 2] = (byte)(255 - inverted[offset + 2]);
        }

        var diagnostics = new SvgDiagnostics();
        Assert.False(SdfBc7Validator.ValidateLevel(field, levels[1], inverted, 1, diagnostics));

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfCompressionFailed, error.Code);
        Assert.Contains("sign", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bc7SkipsTheDecodedSignCheckBelowFourPixels()
    {
        MsdfField field = CreateDonutField();
        var tiny = new TextureLevel(2, 1, new byte[2 * 4]);
        var decoded = new byte[2 * 4];
        Array.Fill(decoded, (byte)255);

        var diagnostics = new SvgDiagnostics();
        Assert.True(SdfBc7Validator.ValidateLevel(field, tiny, decoded, 5, diagnostics));
        Assert.False(diagnostics.HasErrors);
    }

    private static MsdfField CreateDonutField()
    {
        SvgArtwork artwork = SvgSdfTestSupport.Parse(SvgSdfTestSupport.DonutPath);
        var diagnostics = new SvgDiagnostics();
        MsdfField? field = MsdfField.Create(artwork, 0.54, 10, 128, 74, 8, diagnostics);
        Assert.NotNull(field);
        Assert.False(diagnostics.HasErrors);
        return field!;
    }

    private static int BlockLength(int width, int height)
        => ((width + 3) / 4) * ((height + 3) / 4) * 16;

    private static (double Maximum, double Rms) MedianErrors(
        TextureLevel source,
        IReadOnlyList<ColorRgba32> decoded,
        int range)
    {
        double maximum = 0;
        double sumSquares = 0;
        int pixelCount = source.Width * source.Height;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int offset = pixel * 4;
            double sourceMedian = SvgSdfTestSupport.Median(
                source.Pixels[offset],
                source.Pixels[offset + 1],
                source.Pixels[offset + 2]);
            double decodedMedian = SvgSdfTestSupport.Median(
                decoded[pixel].r,
                decoded[pixel].g,
                decoded[pixel].b);
            double error = Math.Abs(sourceMedian - decodedMedian) * range;
            maximum = Math.Max(maximum, error);
            sumSquares += error * error;
        }

        return (maximum, Math.Sqrt(sumSquares / pixelCount));
    }

    private static void AssertDecodedSigns(
        Ktx2TestDocument document,
        int level,
        int holeX,
        int holeY,
        int ringX,
        int ringY)
    {
        int width = TextureMipChain.LevelWidth((int)document.Width, level);
        int height = TextureMipChain.LevelHeight((int)document.Height, level);
        ColorRgba32[] decoded = new BcDecoder().DecodeRaw(
            document.GetLevelBytes(level),
            width,
            height,
            CompressionFormat.Bc7);

        Assert.True(MedianAt(decoded, width, holeX, holeY) < 0.5);
        Assert.True(MedianAt(decoded, width, ringX, ringY) > 0.5);
        Assert.True(MedianAt(decoded, width, 0, 0) < 0.5);
    }

    private static double MedianAt(ColorRgba32[] decoded, int width, int x, int y)
    {
        ColorRgba32 pixel = decoded[(y * width) + x];
        return SvgSdfTestSupport.Median(pixel.r, pixel.g, pixel.b);
    }
}
