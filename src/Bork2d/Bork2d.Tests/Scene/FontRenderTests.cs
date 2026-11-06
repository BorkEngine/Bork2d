using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Deterministic tests for font renderables: creation defaults, layout, alignment, line breaks,
/// kerning, missing glyphs, invalid layout metrics, caching, and asset freezing.
/// </summary>
public sealed class FontRenderTests
{
    [Fact]
    public void FontRenderHasTheDeclaredDefaults()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();

        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);

        Assert.Null(font.Font);
        Assert.Equal(string.Empty, font.Text);
        Assert.Equal(1f, font.Size);
        Assert.Equal(TextAlignment.Left, font.Alignment);
        Assert.Equal(1f, font.LineSpacing);
        Assert.Equal(Color.White, font.Color);
        Assert.Null(font.Shader);
        Assert.Equal(GameLayer.None, font.Layer);
        Assert.Equal(0, font.ZIndex);
        Assert.Same(engine.Root, font.Parent);
        Assert.Equal(Bounds.Empty, font.Bounds);
    }

    [Fact]
    public void CreateFontRenderRequiresAnEngineParent()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();

        Assert.Throws<ArgumentNullException>(() => { engine.CreateFontRender(null!); });
        Assert.Throws<Bork2dException>(() => { engine.CreateFontRender(new FakeTransform()); });
    }

    [Fact]
    public void LayoutOfABPlacesQuadsAndAdvancesThePen()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";

        FontLayoutData layout = Layout(engine);

        Assert.False(layout.IsEmpty);
        Assert.Equal(2, layout.Glyphs.Length);
        TestAssertions.Close(1.25f, layout.Width);
        TestAssertions.Close(FontTestSupport.LineHeight, layout.Height);
        AssertQuad(layout.Glyphs[0], new Vector2(0.05f, 0f), new Vector2(0.5f, 0.6f), FontTestSupport.AAtlas);
        AssertQuad(layout.Glyphs[1], new Vector2(0.62f, -0.05f), new Vector2(0.56f, 0.6f), FontTestSupport.BAtlas);
        TestAssertions.Close(new Vector2(0.05f, -0.05f), layout.LocalBounds.Min);
        TestAssertions.Close(new Vector2(1.18f, 0.6f), layout.LocalBounds.Max);
    }

    [Fact]
    public void SizeScalesAdvancesQuadsAndLineHeight()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";
        font.Size = 2f;

        FontLayoutData layout = Layout(engine);

        TestAssertions.Close(2.5f, layout.Width);
        TestAssertions.Close(FontTestSupport.LineHeight * 2f, layout.Height);
        AssertQuad(layout.Glyphs[0], new Vector2(0.1f, 0f), new Vector2(1f, 1.2f), FontTestSupport.AAtlas);
        AssertQuad(
            layout.Glyphs[1],
            new Vector2(1.24f, -0.1f),
            new Vector2(1.12f, 1.2f),
            FontTestSupport.BAtlas);
    }

    [Theory]
    [InlineData(TextAlignment.Left, 0.05f, 0.62f)]
    [InlineData(TextAlignment.Center, -0.575f, -0.005f)]
    [InlineData(TextAlignment.Right, -1.2f, -0.63f)]
    public void AlignmentShiftsTheWholeLineAroundTheOrigin(
        TextAlignment alignment,
        float expectedA,
        float expectedB)
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";
        font.Alignment = alignment;

        FontLayoutData layout = Layout(engine);

        TestAssertions.Close(expectedA, layout.Glyphs[0].Min.X);
        TestAssertions.Close(expectedB, layout.Glyphs[1].Min.X);
        TestAssertions.Close(1.25f, layout.Width);
    }

    [Fact]
    public void AlignmentIsAppliedPerLineWithThatLinesWidth()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A\nBB";
        font.Alignment = TextAlignment.Center;

        FontLayoutData layout = Layout(engine);

        Assert.Equal(3, layout.Glyphs.Length);
        TestAssertions.Close(-0.25f, layout.Glyphs[0].Min.X);
        TestAssertions.Close(-0.63f, layout.Glyphs[1].Min.X);
        TestAssertions.Close(0.02f, layout.Glyphs[2].Min.X);
    }

    [Theory]
    [InlineData("A\nB")]
    [InlineData("A\r\nB")]
    [InlineData("A\rB")]
    public void LineBreaksStartANewLineAtTheNextBaseline(string text)
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = text;

        FontLayoutData layout = Layout(engine);

        Assert.Equal(2, layout.Glyphs.Length);
        TestAssertions.Close(FontTestSupport.LineHeight * 2f, layout.Height);
        TestAssertions.Close(0.65f, layout.Width);
        TestAssertions.Close(0f, layout.Glyphs[0].Min.Y);
        TestAssertions.Close(-1.1f, layout.Glyphs[1].Min.Y);
    }

    [Theory]
    [InlineData("A\n")]
    [InlineData("A\r\n")]
    [InlineData("A\r")]
    public void ATrailingLineBreakContributesAFinalEmptyLine(string text)
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = text;

        FontLayoutData layout = Layout(engine);

        Assert.Equal(1, layout.Glyphs.Length);
        TestAssertions.Close(0.6f, layout.Width);
        TestAssertions.Close(FontTestSupport.LineHeight * 2f, layout.Height);
    }

    [Fact]
    public void LineSpacingIsAMultiplierOfTheDefaultLineHeight()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A\nB";
        font.LineSpacing = 2f;

        FontLayoutData layout = Layout(engine);

        TestAssertions.Close(4.2f, layout.Height);
        TestAssertions.Close(-2.15f, layout.Glyphs[1].Min.Y);
    }

    [Fact]
    public void KerningAdjustsTheNextEffectiveGlyphsPenPosition()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AV";

        FontLayoutData layout = Layout(engine);

        TestAssertions.Close(1.12f, layout.Width);
        TestAssertions.Close(0.05f, layout.Glyphs[0].Min.X);
        TestAssertions.Close(0.53f, layout.Glyphs[1].Min.X);
    }

    [Fact]
    public void SkippedCodepointsAreAbsentForKerning()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont(includeQuestionMark: false);
        font.Text = "AXV";

        FontLayoutData layout = Layout(engine);

        Assert.Equal(2, layout.Glyphs.Length);
        TestAssertions.Close(1.12f, layout.Width);
        TestAssertions.Close(0.53f, layout.Glyphs[1].Min.X);
    }

    [Fact]
    public void MissingCodepointSubstitutesTheQuestionMarkGlyph()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AC";

        FontLayoutData layout = Layout(engine);

        Assert.Equal(2, layout.Glyphs.Length);
        TestAssertions.Close(1.1f, layout.Width);
        AssertQuad(
            layout.Glyphs[1],
            new Vector2(0.7f, -0.1f),
            new Vector2(0.35f, 0.6f),
            FontTestSupport.QuestionAtlas);
    }

    [Fact]
    public void MissingCodepointWithoutQuestionMarkIsSkippedAndWarnedOnce()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = FontTestSupport.CreateLoggerFactory(provider);
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine(loggerFactory);
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont(includeQuestionMark: false);
        font.Text = "C";

        Assert.Equal(Bounds.Empty, font.Bounds);
        FrameSnapshot snapshot = FontTestSupport.Capture(engine);

        Assert.Equal(1, snapshot.FontCount);
        Assert.Equal(Bounds.Empty, snapshot.Fonts[0].Bounds);
        FontLayoutData layout = Assert.IsType<FontLayoutData>(snapshot.Fonts[0].Layout);
        Assert.True(layout.IsEmpty);
        TestAssertions.Close(0f, layout.Width);
        TestAssertions.Close(FontTestSupport.LineHeight, layout.Height);
        Assert.Single(Warnings(provider));

        font.Text = "D";
        _ = FontTestSupport.Capture(engine);
        Assert.Equal(2, Warnings(provider).Count);

        font.Text = "C";
        _ = FontTestSupport.Capture(engine);
        Assert.Equal(2, Warnings(provider).Count);

        CapturingLoggerProvider.CapturedLog warning = Warnings(provider)[0];
        Assert.Contains("U+0043", warning.Message, StringComparison.Ordinal);
        Assert.Contains("skipped", warning.Message, StringComparison.Ordinal);
        Assert.Equal("Bork2d.Engine.Scene.Font", warning.Category);
    }

    [Fact]
    public void InvalidSizeProducesAnEmptyLayoutAndOneWarning()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = FontTestSupport.CreateLoggerFactory(provider);
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine(loggerFactory);
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";

        float[] invalidSizes = [0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        foreach (float size in invalidSizes)
        {
            font.Size = size;
            FrameSnapshot snapshot = FontTestSupport.Capture(engine);
            Assert.Same(FontLayoutData.Empty, snapshot.Fonts[0].Layout);
            Assert.Equal(Bounds.Empty, snapshot.Fonts[0].Bounds);
        }

        Assert.Single(Warnings(provider));

        font.Size = 1f;
        Assert.False(Layout(engine).IsEmpty);
        font.Size = 0f;
        Assert.Same(FontLayoutData.Empty, Layout(engine));
        Assert.Single(Warnings(provider));
    }

    [Fact]
    public void InvalidLineSpacingProducesAnEmptyLayoutAndOneWarning()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = FontTestSupport.CreateLoggerFactory(provider);
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine(loggerFactory);
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";

        float[] invalidSpacing = [0f, -1f, float.NaN, float.PositiveInfinity];
        foreach (float spacing in invalidSpacing)
        {
            font.LineSpacing = spacing;
            Assert.Same(FontLayoutData.Empty, Layout(engine));
        }

        Assert.Single(Warnings(provider));
    }

    [Fact]
    public void InvalidFontMetricsProduceAnEmptyLayoutAndOneWarning()
    {
        var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = FontTestSupport.CreateLoggerFactory(provider);
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine(loggerFactory);
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont(ascent: float.NaN);
        font.Text = "A";

        Assert.Equal(Bounds.Empty, font.Bounds);
        Assert.Same(FontLayoutData.Empty, Layout(engine));
        Assert.Single(Warnings(provider));
    }

    [Fact]
    public void UnknownAlignmentBehavesAsLeft()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "AB";
        FontGlyphQuad left = Layout(engine).Glyphs[0];

        font.Alignment = (TextAlignment)99;

        FontLayoutData layout = Layout(engine);
        Assert.Equal((TextAlignment)99, font.Alignment);
        TestAssertions.Close(left.Min, layout.Glyphs[0].Min);
        TestAssertions.Close(left.Size, layout.Glyphs[0].Size);
    }

    [Fact]
    public void LayoutIsCachedAndRebuiltOnlyWhenItsInputsChange()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        FontAsset asset = FontTestSupport.CreateFont();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = asset;
        font.Text = "AB";
        FontLayoutData first = Layout(engine);
        Assert.Same(first, Layout(engine));

        font.Color = Color.Red;
        font.Layer = GameLayer.Player;
        font.ZIndex = 4;
        font.Shader = FontTestSupport.CreateFloatShader();
        font.LocalPosition = new Vector2(3f, 4f);
        font.LocalRotation = 30f;
        font.LocalScale = new Vector2(2f, 3f);
        Assert.Same(first, Layout(engine));

        font.Text = "BA";
        FontLayoutData textChanged = Layout(engine);
        Assert.NotSame(first, textChanged);
        font.Text = "BA";
        Assert.Same(textChanged, Layout(engine));

        font.Size = 2f;
        FontLayoutData sizeChanged = Layout(engine);
        Assert.NotSame(textChanged, sizeChanged);
        font.Size = 2f;
        Assert.Same(sizeChanged, Layout(engine));

        font.Alignment = TextAlignment.Center;
        FontLayoutData alignmentChanged = Layout(engine);
        Assert.NotSame(sizeChanged, alignmentChanged);
        font.Alignment = TextAlignment.Center;
        Assert.Same(alignmentChanged, Layout(engine));

        font.LineSpacing = 2f;
        FontLayoutData spacingChanged = Layout(engine);
        Assert.NotSame(alignmentChanged, spacingChanged);
        font.LineSpacing = 2f;
        Assert.Same(spacingChanged, Layout(engine));

        FontAsset replacement = FontTestSupport.CreateFont(aAdvance: 2f);
        font.Font = replacement;
        FontLayoutData fontChanged = Layout(engine);
        Assert.NotSame(spacingChanged, fontChanged);
        font.Font = replacement;
        Assert.Same(fontChanged, Layout(engine));
    }

    [Fact]
    public void FreezingKeepsTheFirstLayoutAndBoundsAfterAssetMutation()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        FontAsset asset = FontTestSupport.CreateFont();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = asset;
        font.Text = "A";
        font.LocalPosition = new Vector2(2f, 3f);

        FontSnapshot first = FontTestSupport.Capture(engine).Fonts[0];
        FontLayoutData firstLayout = Assert.IsType<FontLayoutData>(first.Layout);
        FontRenderData firstData = Assert.IsType<FontRenderData>(first.FontData);
        Bounds firstBounds = first.Bounds;

        int aIndex = Array.FindIndex(asset.Glyphs, glyph => glyph.Codepoint == 'A');
        asset.Glyphs[aIndex] = new FontGlyph(
            'A',
            9f,
            new Vector2(1f, 2f),
            new Vector4(1f, 2f, 9f, 9f),
            FontTestSupport.AAtlas);
        AssertUnchanged(engine, firstLayout, firstData, firstBounds);

        asset.Glyphs = [new FontGlyph('B', 7f, Vector2.Zero, FontTestSupport.BPlane, FontTestSupport.BAtlas)];
        asset.Kerning = [];
        asset.Ascent = 99f;
        asset.Descent = -99f;
        asset.LineGap = 99f;
        asset.Atlas = "fonts/other-atlas";
        AssertUnchanged(engine, firstLayout, firstData, firstBounds);

        font.Font = asset;
        AssertUnchanged(engine, firstLayout, firstData, firstBounds);

        FontAsset replacement = FontTestSupport.CreateFont(aAdvance: 2f);
        font.Font = replacement;
        FontSnapshot changed = FontTestSupport.Capture(engine).Fonts[0];
        Assert.NotSame(firstLayout, changed.Layout);
        Assert.NotSame(firstData, changed.FontData);
        TestAssertions.Close(2f, Assert.IsType<FontLayoutData>(changed.Layout).Width);
    }

    private static void AssertUnchanged(
        GameEngine<GameLayer> engine,
        FontLayoutData expectedLayout,
        FontRenderData expectedData,
        Bounds expectedBounds)
    {
        FontSnapshot captured = FontTestSupport.Capture(engine).Fonts[0];
        Assert.Same(expectedLayout, captured.Layout);
        Assert.Same(expectedData, captured.FontData);
        TestAssertions.Close(expectedBounds.Min, captured.Bounds.Min);
        TestAssertions.Close(expectedBounds.Max, captured.Bounds.Max);
    }

    private static FontLayoutData Layout(GameEngine<GameLayer> engine)
        => Assert.IsType<FontLayoutData>(FontTestSupport.Capture(engine).Fonts[0].Layout);

    private static IReadOnlyList<CapturingLoggerProvider.CapturedLog> Warnings(
        CapturingLoggerProvider provider)
        => provider.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .ToArray();

    private static void AssertQuad(
        FontGlyphQuad quad,
        Vector2 expectedMin,
        Vector2 expectedSize,
        Vector4 expectedUv)
    {
        TestAssertions.Close(expectedMin, quad.Min);
        TestAssertions.Close(expectedSize, quad.Size);
        Assert.Equal(expectedUv, quad.Uv);
    }
}
