using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Fill paint and fill-rule handling: <c>fill="none"</c>/<c>transparent</c>, inheritance, ignored
/// paint, and the rounded-rectangle error.
/// </summary>
public sealed class SvgFillTests
{
    [Fact]
    public void FillNoneProducesNoGeometry()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"none\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void FillTransparentProducesNoGeometry()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"transparent\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void RootFillNoneSuppressesEveryChild()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\" fill=\"none\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void InheritedFillNoneSuppressesUnpaintedChildren()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<g fill=\"none\"><rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" /></g></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void ChildFillOverridesAnInheritedNone()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<g fill=\"none\"><rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"red\" /></g>");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void FillNoneOnOneShapeKeepsTheOtherShapes()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<rect x=\"10\" y=\"10\" width=\"20\" height=\"20\" fill=\"none\" />"
                + "<rect x=\"40\" y=\"40\" width=\"20\" height=\"20\" />");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void StrokeOnlyShapeIsEmpty()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"none\" "
                + "stroke=\"black\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void GradientFillIsTreatedAsPaintedGeometry()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"url(#gradient)\" />");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void InvalidFillRuleIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill-rule=\"winding\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("fill-rule", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InheritedFillRuleAppliesToChildren()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<g fill-rule=\"evenodd\">"
                + "<path d=\"M 0 0 H 100 V 100 H 0 Z M 25 25 H 75 V 75 H 25 Z\" /></g>");

        SvgShape shape = SvgTestSupport.SingleShape(artwork);
        Assert.Equal(SvgFillRule.EvenOdd, shape.FillRule);
        Assert.Equal(2, shape.Contours.Count);
        Assert.True(SvgTestSupport.SignedArea(shape.Contours[0]) > 0);
        Assert.True(SvgTestSupport.SignedArea(shape.Contours[1]) < 0);
    }

    [Fact]
    public void RoundedRectangleIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" rx=\"5\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("rounded", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundedRectangleWithOnlyRyIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" ry=\"5\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void ZeroCornerRadiusKeepsTheSharpRectangle()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" rx=\"0\" ry=\"0\" />");

        SvgShape shape = SvgTestSupport.SingleShape(artwork);
        SvgTestSupport.AssertNear(2500, SvgTestSupport.SignedArea(Assert.Single(shape.Contours)));
    }

    [Fact]
    public void NegativeCornerRadiusIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 100 100\">"
                + "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" rx=\"-1\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }
}
