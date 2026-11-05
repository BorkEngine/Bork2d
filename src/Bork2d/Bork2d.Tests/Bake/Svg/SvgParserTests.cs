using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Document-level parsing: XML errors, the root element rule, ignored and unsupported elements,
/// namespaces, diagnostic locations, and error collection.
/// </summary>
public sealed class SvgParserTests
{
    [Fact]
    public void CommentsTitleAndDescAreIgnoredSilently()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\"><!-- comment --><title>T</title><desc>D</desc>"
                + "<rect x=\"10\" y=\"10\" width=\"20\" height=\"20\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Empty(diagnostics.Items);
        Assert.Single(artwork!.Shapes);
    }

    [Fact]
    public void UnsupportedElementIsSkippedWithOneInfo()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\"><text x=\"0\" y=\"0\">hi</text>"
                + "<rect x=\"10\" y=\"10\" width=\"20\" height=\"20\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Single(artwork!.Shapes);
        SvgDiagnostic info = Assert.Single(diagnostics.Items);
        Assert.Equal(SvgDiagnosticSeverity.Info, info.Severity);
        Assert.Equal(SvgDiagnosticCodes.FeatureIgnored, info.Code);
        Assert.Contains("'text'", info.Message, StringComparison.Ordinal);
        Assert.Equal(1, info.Line);
        Assert.NotNull(info.Column);
    }

    [Fact]
    public void UnsupportedElementSubtreeIsSkipped()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\"><a href=\"#x\"><rect width=\"5\" height=\"5\" /></a>"
                + "<rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Single(artwork!.Shapes);
        SvgDiagnostic info = Assert.Single(diagnostics.Items);
        Assert.Equal(SvgDiagnosticCodes.FeatureIgnored, info.Code);
        Assert.Contains("'a'", info.Message, StringComparison.Ordinal);
        SvgTestSupport.AssertNear(
            20,
            SvgTestSupport.Bounds(artwork.Shapes[0].Contours).MinX);
    }

    [Fact]
    public void DefsContentIsNotDrawn()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\"><defs><rect width=\"10\" height=\"10\" /></defs>"
                + "<rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Single(artwork!.Shapes);
        Assert.Single(diagnostics.Items);
        Assert.Equal(SvgDiagnosticCodes.FeatureIgnored, diagnostics.Items[0].Code);
        SvgTestSupport.AssertNear(
            20,
            SvgTestSupport.Bounds(artwork.Shapes[0].Contours).MinX);
    }

    [Fact]
    public void UseIsNotExpanded()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\"><defs>"
                + "<path id=\"p\" d=\"M 0 0 H 10 V 10 H 0 Z\" /></defs><use href=\"#p\" />"
                + "<rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Single(artwork!.Shapes);
        Assert.Equal(2, diagnostics.Items.Count);
        Assert.All(
            diagnostics.Items,
            diagnostic => Assert.Equal(SvgDiagnosticCodes.FeatureIgnored, diagnostic.Code));
    }

    [Fact]
    public void NestedSvgIsSkippedWithInfo()
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = SvgTestSupport.ParseWithDiagnostics(
            "<svg viewBox=\"0 0 100 100\">"
                + "<svg viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\" /></svg>"
                + "<rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" /></svg>");

        Assert.NotNull(artwork);
        Assert.Single(artwork!.Shapes);
        SvgDiagnostic info = Assert.Single(diagnostics.Items);
        Assert.Contains("nested", info.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedXmlIsBK9201()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg><rect></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SourceXmlInvalid, error.Code);
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void NonSvgRootIsBK9201()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError("<html />", out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.SourceXmlInvalid, error.Code);
        Assert.Contains("html", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhitespaceSourceIsBK9201()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError("   ", out _);

        Assert.Equal(SvgDiagnosticCodes.SourceXmlInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void EmptyDocumentIsBK9205()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\" />",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void InvalidTransformReportsTheElementLocation()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\">\n  <rect width=\"5\" height=\"5\" transform=\"skewW(1)\" />\n</svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Equal(2, error.Line);
        Assert.NotNull(error.Column);
        Assert.Contains("transform", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PathErrorIncludesTheCharacterOffsetAndElementLine()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\">\n  <path d=\"M 0 0 L\" />\n</svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Equal(2, error.Line);
        Assert.Contains("path data", error.Message, StringComparison.Ordinal);
        Assert.Contains("offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryErrorIsCollectedBeforeReturningNull()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\">"
                + "<rect height=\"5\" /><rect height=\"5\" /></svg>",
            out _);

        int errors = 0;
        foreach (SvgDiagnostic diagnostic in diagnostics.Items)
        {
            if (diagnostic.Severity == SvgDiagnosticSeverity.Error)
            {
                errors++;
            }
        }

        Assert.Equal(2, errors);
    }

    [Fact]
    public void PrefixedNamespaceStillParses()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">"
                + "<svg:rect width=\"10\" height=\"10\" /></svg:svg>");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        var diagnostics = new SvgDiagnostics();

        Assert.Throws<ArgumentNullException>(() => SvgParser.TryParse(null!, "test.svg", diagnostics));
        Assert.Throws<ArgumentNullException>(() => SvgParser.TryParse("<svg />", null!, diagnostics));
        Assert.Throws<ArgumentNullException>(() => SvgParser.TryParse("<svg />", "test.svg", null!));
    }
}
