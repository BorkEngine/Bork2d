using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Assets.Fonts;

/// <summary>
/// Deterministic tests for <see cref="FontAsset.MeasureText(ReadOnlySpan{char}, float)"/>: the
/// layout box, line height, kerning, missing glyphs, invalid sizes, and current-field semantics.
/// </summary>
public sealed class FontAssetMeasureTextTests
{
    [Fact]
    public void MeasureTextReturnsTheWidestLineAdvanceAndTheLineHeight()
    {
        FontAsset asset = FontTestSupport.CreateFont();

        Vector2 size = asset.MeasureText("AB", 1f);

        TestAssertions.Close(new Vector2(1.25f, FontTestSupport.LineHeight), size);
    }

    [Theory]
    [InlineData("A\nB", 0.65f, 2.1f)]
    [InlineData("A\r\nB", 0.65f, 2.1f)]
    [InlineData("A\rB", 0.65f, 2.1f)]
    [InlineData("A\n", 0.6f, 2.1f)]
    [InlineData("\n", 0f, 2.1f)]
    [InlineData(" ", 0.3f, 1.05f)]
    public void MeasureTextCountsLinesAndTrailingEmptyLines(
        string text,
        float expectedWidth,
        float expectedHeight)
    {
        FontAsset asset = FontTestSupport.CreateFont();

        Vector2 size = asset.MeasureText(text, 1f);

        TestAssertions.Close(new Vector2(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void MeasureTextIncludesKerningAndSpaces()
    {
        FontAsset asset = FontTestSupport.CreateFont();

        Vector2 kerned = asset.MeasureText("AV", 1f);
        Vector2 spaced = asset.MeasureText(" A", 1f);

        TestAssertions.Close(new Vector2(1.12f, FontTestSupport.LineHeight), kerned);
        TestAssertions.Close(new Vector2(0.9f, FontTestSupport.LineHeight), spaced);
    }

    [Fact]
    public void EmptyTextAndInvalidSizeMeasureAsZero()
    {
        FontAsset asset = FontTestSupport.CreateFont();

        Assert.Equal(Vector2.Zero, asset.MeasureText(string.Empty, 1f));
        Assert.Equal(Vector2.Zero, asset.MeasureText("AB", 0f));
        Assert.Equal(Vector2.Zero, asset.MeasureText("AB", -1f));
        Assert.Equal(Vector2.Zero, asset.MeasureText("AB", float.NaN));
        Assert.Equal(Vector2.Zero, asset.MeasureText("AB", float.PositiveInfinity));
        Assert.Equal(Vector2.Zero, asset.MeasureText("AB", float.NegativeInfinity));
    }

    [Fact]
    public void MeasureTextUsesTheSameMissingGlyphRules()
    {
        FontAsset withFallback = FontTestSupport.CreateFont();
        FontAsset withoutFallback = FontTestSupport.CreateFont(includeQuestionMark: false);

        Vector2 substituted = withFallback.MeasureText("C", 1f);
        Vector2 skipped = withoutFallback.MeasureText("C", 1f);

        TestAssertions.Close(
            new Vector2(FontTestSupport.QuestionAdvance, FontTestSupport.LineHeight),
            substituted);
        TestAssertions.Close(new Vector2(0f, FontTestSupport.LineHeight), skipped);
    }

    [Fact]
    public void MeasureTextReflectsTheCurrentAssetFieldsAndDoesNotFreeze()
    {
        FontAsset asset = FontTestSupport.CreateFont();
        TestAssertions.Close(0.6f, asset.MeasureText("A", 1f).X);

        int aIndex = Array.FindIndex(asset.Glyphs, glyph => glyph.Codepoint == 'A');
        asset.Glyphs[aIndex] = new FontGlyph(
            'A',
            1.2f,
            new Vector2(0.05f, 0f),
            FontTestSupport.APlane,
            FontTestSupport.AAtlas);
        TestAssertions.Close(1.2f, asset.MeasureText("A", 1f).X);

        asset.Ascent = 1.3f;
        TestAssertions.Close(1.55f, asset.MeasureText("A", 1f).Y);

        asset.Kerning = [new FontKerning('A', 'V', -0.3f)];
        TestAssertions.Close(1.52f, asset.MeasureText("AV", 1f).X);
    }

    [Fact]
    public void MeasureTextToleratesNullArraysAndMalformedGlyphs()
    {
        FontAsset nullArrays = FontTestSupport.CreateFont();
        nullArrays.Glyphs = null!;
        nullArrays.Kerning = null!;

        TestAssertions.Close(new Vector2(0f, FontTestSupport.LineHeight), nullArrays.MeasureText("A", 1f));

        FontAsset malformed = FontTestSupport.CreateFont();
        malformed.Glyphs =
        [
            new FontGlyph(
                'A',
                float.NaN,
                Vector2.Zero,
                FontTestSupport.APlane,
                FontTestSupport.AAtlas),
        ];

        TestAssertions.Close(new Vector2(0f, FontTestSupport.LineHeight), malformed.MeasureText("A", 1f));

        malformed.Ascent = float.NaN;
        Assert.Equal(Vector2.Zero, malformed.MeasureText("A", 1f));
    }
}
