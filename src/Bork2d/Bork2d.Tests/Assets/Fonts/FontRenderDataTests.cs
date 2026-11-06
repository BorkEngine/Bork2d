using System.Numerics;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Assets.Fonts;

/// <summary>
/// Deterministic tests for the immutable <see cref="FontRenderData"/> freeze: cloned arrays,
/// deterministic ordering, first-entry duplicate handling, lookups, and mutation isolation.
/// </summary>
public sealed class FontRenderDataTests
{
    [Fact]
    public void FreezeCopiesSortsAndDeduplicatesGlyphsAndKerning()
    {
        var asset = new FontAsset
        {
            Atlas = "fonts/source-atlas",
            PixelsPerEm = 16,
            Ascent = 0.7f,
            Descent = -0.3f,
            LineGap = 0.1f,
            DistanceRange = 4f,
            Glyphs =
            [
                Glyph('B', 0.7f),
                Glyph('A', 0.6f),
                Glyph('B', 9f),
            ],
            Kerning =
            [
                new FontKerning('A', 'V', -0.2f),
                new FontKerning('A', 'B', -0.1f),
                new FontKerning('A', 'V', -9f),
            ],
        };

        FontRenderData frozen = FontRenderData.Freeze(asset);

        Assert.Equal(new[] { (int)'A', (int)'B' }, frozen.Glyphs.Select(glyph => glyph.Codepoint));
        Assert.Equal(0.6f, frozen.Glyphs[0].Advance);
        Assert.Equal(0.7f, frozen.Glyphs[1].Advance);
        Assert.Equal(
            new[]
            {
                ((int)'A', (int)'B', -0.1f),
                ((int)'A', (int)'V', -0.2f),
            },
            frozen.Kerning.Select(pair => (pair.LeftCodepoint, pair.RightCodepoint, pair.Adjustment)));
        Assert.Equal("fonts/source-atlas", frozen.Atlas.Path);
        Assert.Equal(16u, frozen.PixelsPerEm);
        Assert.Equal(0.7f, frozen.Ascent);
        Assert.Equal(-0.3f, frozen.Descent);
        Assert.Equal(0.1f, frozen.LineGap);
        Assert.Equal(4f, frozen.DistanceRange);
    }

    [Fact]
    public void FreezeIsPerAssetAndIgnoresLaterMutation()
    {
        FontAsset asset = CreateMinimalAsset();
        FontRenderData first = FontRenderData.Freeze(asset);
        FontRenderData again = FontRenderData.Freeze(asset);
        var different = new FontAsset();

        Assert.Same(first, again);
        Assert.NotSame(first, FontRenderData.Freeze(different));

        asset.Glyphs[0] = Glyph('C', 9f);
        asset.Glyphs = [Glyph('D', 8f)];
        asset.Kerning = [];
        asset.Ascent = 99f;
        asset.Atlas = "fonts/changed";

        Assert.Equal(new[] { (int)'A' }, first.Glyphs.Select(glyph => glyph.Codepoint));
        Assert.Equal(0.6f, first.Glyphs[0].Advance);
        Assert.Equal(0.7f, first.Ascent);
        Assert.Equal("fonts/source-atlas", first.Atlas.Path);
    }

    [Fact]
    public void LookupsAreOrdinalAndTolerateNullArrays()
    {
        FontAsset asset = CreateMinimalAsset();
        FontRenderData frozen = FontRenderData.Freeze(asset);

        Assert.True(frozen.TryGetGlyph('A', out FontGlyph glyph));
        Assert.Equal(0.6f, glyph.Advance);
        Assert.False(frozen.TryGetGlyph('Z', out _));

        Assert.True(frozen.TryGetKerning('A', 'V', out float adjustment));
        Assert.Equal(-0.2f, adjustment);
        Assert.False(frozen.TryGetKerning('V', 'A', out float missingAdjustment));
        Assert.Equal(0f, missingAdjustment);

        var malformed = new FontAsset { Glyphs = null!, Kerning = null! };
        FontRenderData empty = FontRenderData.Freeze(malformed);

        Assert.Empty(empty.Glyphs);
        Assert.Empty(empty.Kerning);
        Assert.False(empty.TryGetGlyph('A', out _));
        Assert.False(empty.TryGetKerning('A', 'V', out _));
    }

    [Fact]
    public void NullAssetIsRejectedByFreezeAndAllowedByFreezeOrNull()
    {
        Assert.Throws<ArgumentNullException>(() => FontRenderData.Freeze(null!));
        Assert.Null(FontRenderData.FreezeOrNull(null));
    }

    private static FontAsset CreateMinimalAsset() => new()
    {
        Atlas = "fonts/source-atlas",
        PixelsPerEm = 16,
        Ascent = 0.7f,
        Descent = -0.3f,
        LineGap = 0.1f,
        DistanceRange = 4f,
        Glyphs =
        [
            Glyph('A', 0.6f),
        ],
        Kerning =
        [
            new FontKerning('A', 'V', -0.2f),
        ],
    };

    private static FontGlyph Glyph(int codepoint, float advance)
        => new(
            codepoint,
            advance,
            new Vector2(0.05f, 0f),
            new Vector4(0.05f, 0f, 0.55f, 0.6f),
            new Vector4(0f, 0f, 0.25f, 0.5f));
}
