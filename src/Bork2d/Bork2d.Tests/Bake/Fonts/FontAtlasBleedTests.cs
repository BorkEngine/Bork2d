using System.Globalization;
using Bork2d.Bake.Fonts;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Bake.Textures;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// Regression tests for the MSDF cell guard band: the per-cell padding border (or a one-pixel guard
/// when the sidecar asks for no padding) must be black in every channel. Otherwise adjacent cells
/// blend different saturated channels under linear filtering and draw straight lines at cell edges.
/// </summary>
public sealed class FontAtlasBleedTests
{
    private const string FontRelativePath = "examples/pong/Pong.Assets/Assets/fonts/score.ttf";
    private const string PrintableAsciiRange = "U+0020-U+007E";
    private const int Resolution = 384;
    private const int Range = 8;

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void MsdfCellGuardBorderIsBlackInEveryChannel(int padding)
    {
        byte[] source = File.ReadAllBytes(Path.Combine(
            RepositoryPaths.Root,
            FontRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var diagnostics = new FontDiagnostics();
        FontBakeResult? result = FontBakery.Bake(
            source,
            new FontBakeOptions(Resolution, Range, padding),
            ranges: [PrintableAsciiRange],
            glyphs: [],
            atlasAssetPath: "fonts/score-atlas",
            diagnostics);
        if (result is null)
        {
            throw new InvalidOperationException(
                "The font failed to bake: "
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.Items.Select(item => $"{item.Code}: {item.Message}")));
        }

        Ktx2TestDocument document = Ktx2TestDocument.Parse(result.Texture.Payload.ToArray());
        byte[] baseLevel = document.GetLevelBytes(0);
        int guard = Math.Max(padding, 1);
        var hasInteriorInk = false;

        foreach (FontGlyph glyph in result.Font.Glyphs)
        {
            if (glyph.AtlasBounds == System.Numerics.Vector4.Zero)
            {
                continue;
            }

            int cellX = (int)Math.Round(glyph.AtlasBounds.X * Resolution);
            int cellY = (int)Math.Round(glyph.AtlasBounds.Y * Resolution);
            int cellWidth = (int)Math.Round(
                (glyph.AtlasBounds.Z - glyph.AtlasBounds.X) * Resolution);
            int cellHeight = (int)Math.Round(
                (glyph.AtlasBounds.W - glyph.AtlasBounds.Y) * Resolution);
            Assert.InRange(cellX, 0, Resolution - cellWidth);
            Assert.InRange(cellY, 0, Resolution - cellHeight);
            Assert.True(cellWidth >= 2 * guard && cellHeight >= 2 * guard);

            for (int y = 0; y < cellHeight; y++)
            {
                for (int x = 0; x < cellWidth; x++)
                {
                    bool border = x < guard
                                  || x >= cellWidth - guard
                                  || y < guard
                                  || y >= cellHeight - guard;
                    int offset = (((cellY + y) * Resolution) + cellX + x) * 4;
                    byte red = baseLevel[offset];
                    byte green = baseLevel[offset + 1];
                    byte blue = baseLevel[offset + 2];
                    if (border)
                    {
                        Assert.True(
                            red == 0 && green == 0 && blue == 0,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"Glyph U+{(int)glyph.Codepoint:X4} has a non-black guard texel at "
                                    + $"({x}, {y}) in its cell: ({red}, {green}, {blue})."));
                    }
                    else if (red != 0 || green != 0 || blue != 0)
                    {
                        hasInteriorInk = true;
                    }
                }
            }
        }

        Assert.True(hasInteriorInk, "The atlas should retain non-zero MSDF channels inside the guard.");
    }
}
