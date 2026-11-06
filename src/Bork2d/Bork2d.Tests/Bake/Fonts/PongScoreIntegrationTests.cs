using System.Numerics;
using Bork2d.Bake.Fonts;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// Deterministic integration proof for the Pong score: the authored Pong font bakes with the
/// digits-and-colon selection, its metrics and atlas rectangles are usable, and the baked font
/// measures and aligns the one- and two-digit scores at the game's authoring values.
/// </summary>
/// <remarks>
/// The test bakes the authored <c>examples/pong/Pong.Assets/Assets/fonts/score.ttf</c> at a small
/// resolution through <see cref="FontBakery"/>, so no GPU, generated image, or pack is committed. It
/// mirrors the <c>PongGame</c> authoring values instead of referencing the example assembly.
/// </remarks>
public sealed class PongScoreIntegrationTests
{
    private const string FontRelativePath = "examples/pong/Pong.Assets/Assets/fonts/score.ttf";
    private const string AtlasPath = "fonts/score-atlas";
    private const int AtlasResolution = 128;
    private const int Range = 8;
    private const int Padding = 2;

    // The PongGame authoring values: the score font, the HUD baseline, the center gap, and the court.
    private const float ScoreSize = 1.5f;
    private const float ScoreBaselineY = 7.2f;
    private const float ScoreGap = 0.4f;
    private const float CourtHalfWidth = 16f;
    private const float CourtHalfHeight = 9f;

    private static readonly int[] _scoreCodepoints = [.. Enumerable.Range(0x30, 11)];

    [Fact]
    public void AuthoredFontBakesTheScoreSelectionAndUsableAtlasRectangles()
    {
        FontBakeResult result = BakeScoreFont();

        Assert.Equal(AtlasResolution, result.Resolution);
        Assert.Equal(TextureFormat.Rgb8, result.Texture.Format);
        Assert.True(result.Texture.MipLevels > 1, "The atlas should carry a full mip chain.");
        Assert.Equal(AtlasPath, result.Font.Atlas.Path);

        FontAsset font = result.Font;
        Assert.InRange(font.PixelsPerEm, 8u, (uint)AtlasResolution);
        Assert.Equal(Range, font.DistanceRange);
        Assert.True(font.Ascent > 0f, "The ascent should be positive.");
        Assert.True(font.Descent < 0f, "The descent should be negative.");
        Assert.True(float.IsFinite(font.LineGap));
        Assert.Empty(font.Kerning);

        Assert.Equal(_scoreCodepoints, font.Glyphs.Select(glyph => glyph.Codepoint).ToArray());
        Assert.All(font.Glyphs, glyph =>
        {
            Assert.True(float.IsFinite(glyph.Advance));
            Assert.True(glyph.Advance > 0f, "Every selected glyph should advance.");
            Assert.True(glyph.PlaneBounds.Z > glyph.PlaneBounds.X);
            Assert.True(glyph.PlaneBounds.W > glyph.PlaneBounds.Y);
            Assert.True(glyph.AtlasBounds.Z > glyph.AtlasBounds.X);
            Assert.True(glyph.AtlasBounds.W > glyph.AtlasBounds.Y);
            Assert.InRange(glyph.AtlasBounds.X, 0f, 1f);
            Assert.InRange(glyph.AtlasBounds.Y, 0f, 1f);
            Assert.InRange(glyph.AtlasBounds.Z, 0f, 1f);
            Assert.InRange(glyph.AtlasBounds.W, 0f, 1f);
        });

        // The score is set in a monospaced face, so a two-digit advance is exactly twice a one-digit
        // advance and the score never shifts sideways when a digit is added.
        float advance = font.Glyphs[0].Advance;
        Assert.All(font.Glyphs, glyph => Assert.Equal(advance, glyph.Advance, 4));
    }

    [Fact]
    public void BakedFontMeasuresAndAlignsTheScoreAtThePongValues()
    {
        FontAsset font = BakeScoreFont().Font;
        FontRenderData data = FontRenderData.Freeze(font);
        float lineHeight = (data.Ascent - data.Descent + data.LineGap) * ScoreSize;
        float digitAdvance = data.Glyphs[0].Advance * ScoreSize;

        Assert.True(float.IsFinite(lineHeight) && lineHeight > 0f);
        Assert.True(float.IsFinite(digitAdvance) && digitAdvance > 0f);

        Vector2 measured = font.MeasureText("11", ScoreSize);
        Assert.True(float.IsFinite(measured.X));
        Assert.True(float.IsFinite(measured.Y));
        Assert.Equal(2f * digitAdvance, measured.X, 4);
        Assert.Equal(lineHeight, measured.Y, 4);

        FontLayoutData oneRight = Layout(data, "1", TextAlignment.Right);
        FontLayoutData twoRight = Layout(data, "11", TextAlignment.Right);
        FontLayoutData oneLeft = Layout(data, "1", TextAlignment.Left);
        FontLayoutData twoLeft = Layout(data, "11", TextAlignment.Left);

        Assert.Equal(digitAdvance, oneRight.Width, 4);
        Assert.Equal(2f * digitAdvance, twoRight.Width, 4);
        Assert.Equal(measured.X, twoRight.Width, 4);
        Assert.Equal(lineHeight, twoRight.Height, 4);

        // Right alignment ends the advance edge at the local origin, so the left score's facing edge
        // stays at -ScoreGap however many digits it shows; the left-aligned right score is symmetric.
        float leftScoreEnd = -ScoreGap + LocalAdvanceEnd(twoRight, TextAlignment.Right);
        float rightScoreStart = ScoreGap + LocalAdvanceStart(twoLeft, TextAlignment.Left);
        Assert.Equal(-ScoreGap, leftScoreEnd, 4);
        Assert.Equal(ScoreGap, rightScoreStart, 4);
        Assert.Equal(0f, (leftScoreEnd + rightScoreStart) * 0.5f, 4);
        Assert.Equal(2f * ScoreGap, rightScoreStart - leftScoreEnd, 4);

        // The two-digit pair grows symmetrically away from the center, so it stays centered.
        float leftPairEdge = -ScoreGap + LocalAdvanceStart(twoRight, TextAlignment.Right);
        float rightPairEdge = ScoreGap + LocalAdvanceEnd(twoLeft, TextAlignment.Left);
        Assert.Equal(-ScoreGap - (2f * digitAdvance), leftPairEdge, 4);
        Assert.Equal(ScoreGap + (2f * digitAdvance), rightPairEdge, 4);
        Assert.Equal(0f, (leftPairEdge + rightPairEdge) * 0.5f, 4);

        // The one- and two-digit scores face each other across the same 2 * ScoreGap gap.
        float oneDigitGap = (ScoreGap + LocalAdvanceStart(oneLeft, TextAlignment.Left))
                            - (-ScoreGap + LocalAdvanceEnd(oneRight, TextAlignment.Right));
        Assert.Equal(2f * ScoreGap, oneDigitGap, 4);
        Assert.Equal(oneDigitGap, rightScoreStart - leftScoreEnd, 4);

        // A right-aligned line grows leftward, so the final glyph sits at the same local position
        // for one and two digits; a left-aligned line grows rightward, so the first glyph does.
        Assert.Equal(
            QuadRightEdge(oneRight, oneRight.Glyphs.Length - 1),
            QuadRightEdge(twoRight, twoRight.Glyphs.Length - 1),
            4);
        Assert.Equal(QuadLeftEdge(oneLeft, 0), QuadLeftEdge(twoLeft, 0), 4);

        AssertQuadsInsideCourt(oneRight, -ScoreGap);
        AssertQuadsInsideCourt(twoRight, -ScoreGap);
        AssertQuadsInsideCourt(oneLeft, ScoreGap);
        AssertQuadsInsideCourt(twoLeft, ScoreGap);
    }

    private static FontBakeResult BakeScoreFont()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(
            RepositoryPaths.Root,
            FontRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var diagnostics = new FontDiagnostics();
        FontBakeResult? result = FontBakery.Bake(
            source,
            new FontBakeOptions(Resolution: AtlasResolution, Range: Range, Padding: Padding),
            ranges: ["U+0030-U+0039", "U+003A"],
            glyphs: [],
            atlasAssetPath: AtlasPath,
            diagnostics);
        if (result is null)
        {
            throw new InvalidOperationException(
                "The authored Pong score font failed to bake: "
                    + string.Join(
                        Environment.NewLine,
                        diagnostics.Items.Select(item => $"{item.Code}: {item.Message}")));
        }

        return result;
    }

    private static FontLayoutData Layout(FontRenderData data, string text, TextAlignment alignment)
        => FontLayoutBuilder.Build(
            data.Glyphs,
            data.Kerning,
            data.Ascent,
            data.Descent,
            data.LineGap,
            text,
            ScoreSize,
            alignment,
            lineSpacing: 1f,
            missingGlyph: null);

    private static float LocalAdvanceStart(FontLayoutData layout, TextAlignment alignment)
        => alignment == TextAlignment.Right ? -layout.Width : 0f;

    private static float LocalAdvanceEnd(FontLayoutData layout, TextAlignment alignment)
        => alignment == TextAlignment.Right ? 0f : layout.Width;

    private static float QuadLeftEdge(FontLayoutData layout, int index)
        => layout.Glyphs[index].Min.X;

    private static float QuadRightEdge(FontLayoutData layout, int index)
    {
        FontGlyphQuad quad = layout.Glyphs[index];
        return quad.Min.X + quad.Size.X;
    }

    private static void AssertQuadsInsideCourt(FontLayoutData layout, float originX)
    {
        Assert.False(layout.IsEmpty, "The laid-out score should contain drawable glyph quads.");
        foreach (FontGlyphQuad quad in layout.Glyphs)
        {
            Vector2 min = new Vector2(originX, ScoreBaselineY) + quad.Min;
            Vector2 max = min + quad.Size;
            Assert.InRange(min.X, -CourtHalfWidth, CourtHalfWidth);
            Assert.InRange(max.X, -CourtHalfWidth, CourtHalfWidth);
            Assert.InRange(min.Y, -CourtHalfHeight, CourtHalfHeight);
            Assert.InRange(max.Y, -CourtHalfHeight, CourtHalfHeight);
        }
    }
}
