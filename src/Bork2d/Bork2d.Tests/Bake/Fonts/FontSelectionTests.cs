using System.Globalization;
using Bork2d.Bake.Fonts;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// The font selection parser: inclusive ranges, single glyphs, combination and deduplication, and the
/// malformed, inverted, surrogate, out-of-range, and empty cases.
/// </summary>
public sealed class FontSelectionTests
{
    [Fact]
    public void InclusiveRangesAndGlyphsCombineIntoASortedDistinctSelection()
    {
        var diagnostics = new FontDiagnostics();

        Assert.True(
            FontSelection.TryParse(
                ["U+0030-U+0032", "U+0031-U+0033"],
                ["U+003A", "U+0030"],
                diagnostics,
                out int[] codepoints));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(new[] { 0x30, 0x31, 0x32, 0x33, 0x3A }, codepoints);
    }

    [Fact]
    public void SingleCodepointIsAccepted()
    {
        var diagnostics = new FontDiagnostics();

        Assert.True(FontSelection.TryParse([], ["U+2605"], diagnostics, out int[] codepoints));

        Assert.Equal([0x2605], codepoints);
    }

    [Fact]
    public void LowercasePrefixIsAccepted()
    {
        var diagnostics = new FontDiagnostics();

        Assert.True(FontSelection.TryParse(["u+0041"], [], diagnostics, out int[] codepoints));

        Assert.Equal([0x41], codepoints);
    }

    [Theory]
    [InlineData("U+ZZ")]
    [InlineData("30-39")]
    [InlineData("U+")]
    [InlineData("U+0030-")]
    [InlineData("U+0030-U+ZZ")]
    public void MalformedValuesAreBK9302(string value)
    {
        AssertInvalidSelection([value], []);
    }

    [Fact]
    public void InvertedRangeIsBK9302()
    {
        AssertInvalidSelection(["U+0040-U+0030"], []);
    }

    [Fact]
    public void SurrogateCodepointIsBK9302()
    {
        AssertInvalidSelection(["U+D800"], []);
        AssertInvalidSelection(["U+D800-U+D801"], []);
        AssertInvalidSelection([], ["U+DFFF"]);
    }

    [Fact]
    public void CodepointAboveTheUnicodeMaximumIsBK9302()
    {
        AssertInvalidSelection(["U+110000"], []);
        AssertInvalidSelection([], ["U+110000"]);
    }

    [Fact]
    public void EmptySelectionIsBK9302()
    {
        AssertInvalidSelection([], []);
    }

    private static void AssertInvalidSelection(string[] ranges, string[] glyphs)
    {
        var diagnostics = new FontDiagnostics();

        Assert.False(FontSelection.TryParse(ranges, glyphs, diagnostics, out int[] codepoints));
        Assert.Empty(codepoints);
        FontDiagnostic diagnostic = Assert.Single(
            diagnostics.Items,
            item => item.Code == FontDiagnosticCodes.SelectionInvalid);
        Assert.Equal(FontDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.False(
            string.IsNullOrWhiteSpace(diagnostic.Message),
            string.Create(CultureInfo.InvariantCulture, $"The diagnostic for {string.Join(';', ranges)} must have a message."));
    }
}
