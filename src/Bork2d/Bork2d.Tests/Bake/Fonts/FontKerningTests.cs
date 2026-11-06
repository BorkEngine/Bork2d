using Bork2d.Bake.Fonts;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// The pure kerning-table builder over a fake kerning function: nonzero pairs only, ascending order,
/// missing codepoint filtering, font-unit to em-unit conversion, and tolerance of a throwing font.
/// JetBrains Mono has no kerning pair, so this test supplies the pair data itself instead of adding a
/// binary font fixture.
/// </summary>
public sealed class FontKerningTests
{
    [Fact]
    public void NonzeroPairsAreSortedAndConvertedToEmUnits()
    {
        int[] codepoints = [0x41, 0x56, 0x57];
        var glyphs = new Dictionary<int, int>
        {
            [0x41] = 1,
            [0x56] = 2,
            [0x57] = 3,
        };

        FontKerning[] kerning = FontBakery.BuildKerning(
            codepoints,
            glyphs,
            1000,
            (left, right) => (left, right) switch
            {
                (1, 2) => -50,
                (1, 3) => 25,
                _ => 0,
            });

        Assert.Equal(
            [
                new FontKerning(0x41, 0x56, -0.05f),
                new FontKerning(0x41, 0x57, 0.025f),
            ],
            kerning);
    }

    [Fact]
    public void CodepointsWithoutAGlyphContributeNoPair()
    {
        int[] codepoints = [0x41, 0x1F600, 0x56];
        var glyphs = new Dictionary<int, int>
        {
            [0x41] = 1,
            [0x56] = 2,
        };

        FontKerning[] kerning = FontBakery.BuildKerning(
            codepoints,
            glyphs,
            1000,
            (left, right) => left == 1 && right == 2 ? -100 : 0);

        Assert.Equal([new FontKerning(0x41, 0x56, -0.1f)], kerning);
    }

    [Fact]
    public void ThrowingKerningFunctionContributesNoAdjustment()
    {
        int[] codepoints = [0x41, 0x56];
        var glyphs = new Dictionary<int, int>
        {
            [0x41] = 1,
            [0x56] = 2,
        };

        FontKerning[] kerning = FontBakery.BuildKerning(
            codepoints,
            glyphs,
            1000,
            (left, right) => throw new InvalidOperationException("broken kern table"));

        Assert.Empty(kerning);
    }

    [Fact]
    public void NonPositiveUnitsPerEmIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FontBakery.BuildKerning(
            [0x41],
            new Dictionary<int, int> { [0x41] = 1 },
            0,
            (_, _) => 0));
    }
}
