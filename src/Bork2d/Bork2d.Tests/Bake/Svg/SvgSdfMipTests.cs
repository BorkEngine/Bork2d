using Bork2d.Bake;
using Bork2d.Bake.Svg;
using Bork2d.Bake.Textures;
using Bork2d.Tests.Bake.Textures;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The distance-preserving mip chain: the high mip levels keep the hole and ring signs, and a level
/// that cannot represent the shape is reported as <c>BK9206</c>.
/// </summary>
public sealed class SvgSdfMipTests
{
    [Fact]
    public void MipLevelsPreserveTheHoleAndRingSign()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.DonutPath,
            SvgSdfTestSupport.Options());
        Ktx2TestDocument document = SvgSdfTestSupport.Document(result);

        AssertLevelSigns(document, level: 1, holeX: 32, holeY: 18, ringX: 40, ringY: 18);
        AssertLevelSigns(document, level: 2, holeX: 16, holeY: 8, ringX: 20, ringY: 9);
    }

    [Fact]
    public void ThinStarBakeValidatesItsMipChain()
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            SvgSdfTestSupport.StarPath,
            SvgSdfTestSupport.Options());

        Assert.Equal((uint)TextureMipChain.LevelCount(128, 74), result.Texture.MipLevels);
        Ktx2TestDocument document = SvgSdfTestSupport.Document(result);
        int levelWidth = TextureMipChain.LevelWidth(128, 1);
        int levelHeight = TextureMipChain.LevelHeight(74, 1);
        Assert.Equal(levelWidth * levelHeight * 4, document.GetLevelBytes(1).Length);
    }

    [Fact]
    public void MipLevelThatCannotRepresentTheShapeReportsBK9206()
    {
        SvgArtwork artwork = SvgSdfTestSupport.Parse(SvgSdfTestSupport.DonutPath);
        var fieldDiagnostics = new SvgDiagnostics();
        MsdfField? field = MsdfField.Create(artwork, 0.54, 10, 128, 74, 8, fieldDiagnostics);
        Assert.NotNull(field);
        Assert.False(fieldDiagnostics.HasErrors);

        TextureLevel[] levels = TextureMipChain.Generate(
            field!.Pixels,
            128,
            74,
            TextureChannelType.Rgb);
        var corrupted = new TextureLevel[levels.Length];
        for (int level = 0; level < levels.Length; level++)
        {
            corrupted[level] = new TextureLevel(
                levels[level].Width,
                levels[level].Height,
                [.. levels[level].Pixels]);
        }

        Array.Fill(corrupted[1].Pixels, (byte)0);

        var diagnostics = new SvgDiagnostics();
        Assert.False(field.ValidateMipChain(corrupted, diagnostics));

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SdfBakeFailed, error.Code);
        Assert.Contains("mip level 1", error.Message, StringComparison.Ordinal);
    }

    private static void AssertLevelSigns(
        Ktx2TestDocument document,
        int level,
        int holeX,
        int holeY,
        int ringX,
        int ringY)
    {
        int width = TextureMipChain.LevelWidth((int)document.Width, level);
        int height = TextureMipChain.LevelHeight((int)document.Height, level);
        byte[] pixels = document.GetLevelBytes(level);
        Assert.Equal(width * height * 4, pixels.Length);

        Assert.True(SvgSdfTestSupport.MedianAt(pixels, width, holeX, holeY) < 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(pixels, width, ringX, ringY) > 0.5);
        Assert.True(SvgSdfTestSupport.MedianAt(pixels, width, 0, 0) < 0.5);
    }
}
