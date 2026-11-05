using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Shape-element conversion and geometry validation: rect, circle, ellipse, line, polyline, polygon,
/// and the required/optional attribute rules.
/// </summary>
public sealed class SvgShapeTests
{
    [Fact]
    public void RectBecomesFourLineSegments()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        Assert.Equal(4, contour.Segments.Count);
        Assert.All(contour.Segments, segment => Assert.IsType<SvgLineSegment>(segment));

        SvgBounds bounds = SvgTestSupport.Bounds(contour);
        SvgTestSupport.AssertNear(10, bounds.MinX);
        SvgTestSupport.AssertNear(40, bounds.MaxX);
        SvgTestSupport.AssertNear(40, bounds.MinY);
        SvgTestSupport.AssertNear(80, bounds.MaxY);
        SvgTestSupport.AssertNear(1200, SvgTestSupport.SignedArea(contour));
    }

    [Fact]
    public void RectPositionDefaultsToTheOrigin()
    {
        SvgShape shape = SvgTestSupport.SingleShape(
            SvgTestSupport.ParseDocument("<rect width=\"10\" height=\"20\" />"));

        SvgBounds bounds = SvgTestSupport.Bounds(Assert.Single(shape.Contours));
        SvgTestSupport.AssertNear(0, bounds.MinX);
        SvgTestSupport.AssertNear(80, bounds.MinY);
        SvgTestSupport.AssertNear(10, bounds.MaxX);
        SvgTestSupport.AssertNear(100, bounds.MaxY);
    }

    [Fact]
    public void ShapeLengthsAcceptPxAndApplyTheElementTransform()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<rect x=\"10px\" y=\"20px\" width=\"30px\" height=\"40px\" "
                + "transform=\"translate(5,5)\" />"));

        SvgBounds bounds = SvgTestSupport.Bounds(Assert.Single(shape.Contours));
        SvgTestSupport.AssertNear(15, bounds.MinX);
        SvgTestSupport.AssertNear(45, bounds.MaxX);
        SvgTestSupport.AssertNear(35, bounds.MinY);
        SvgTestSupport.AssertNear(75, bounds.MaxY);
    }

    [Fact]
    public void CircleBecomesFourCubics()
    {
        SvgShape shape = SvgTestSupport.SingleShape(
            SvgTestSupport.ParseDocument("<circle cx=\"50\" cy=\"50\" r=\"10\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        Assert.Equal(4, contour.Segments.Count);
        Assert.All(contour.Segments, segment => Assert.IsType<SvgCubicSegment>(segment));

        SvgBounds bounds = SvgTestSupport.Bounds(contour);
        SvgTestSupport.AssertNear(40, bounds.MinX, 1e-9);
        SvgTestSupport.AssertNear(60, bounds.MaxX, 1e-9);
        SvgTestSupport.AssertNear(40, bounds.MinY, 1e-9);
        SvgTestSupport.AssertNear(60, bounds.MaxY, 1e-9);

        // The four-cubic circle is accurate to about 0.03 percent of the true area.
        SvgTestSupport.AssertNear(Math.PI * 100, SvgTestSupport.SignedArea(contour), 0.1);
    }

    [Fact]
    public void CircleWithZeroRadiusProducesNoGeometry()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<circle cx=\"50\" cy=\"50\" r=\"0\" />"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" />");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void EllipseUsesBothRadii()
    {
        SvgShape shape = SvgTestSupport.SingleShape(
            SvgTestSupport.ParseDocument("<ellipse cx=\"50\" cy=\"50\" rx=\"20\" ry=\"10\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        SvgBounds bounds = SvgTestSupport.Bounds(contour);
        SvgTestSupport.AssertNear(30, bounds.MinX, 1e-9);
        SvgTestSupport.AssertNear(70, bounds.MaxX, 1e-9);
        SvgTestSupport.AssertNear(40, bounds.MinY, 1e-9);
        SvgTestSupport.AssertNear(60, bounds.MaxY, 1e-9);
        SvgTestSupport.AssertNear(Math.PI * 200, SvgTestSupport.SignedArea(contour), 0.5);
    }

    [Fact]
    public void PolylineIsClosedForFill()
    {
        SvgShape shape = SvgTestSupport.SingleShape(
            SvgTestSupport.ParseDocument("<polyline points=\"10,10 30,10 30,30\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        Assert.Equal(3, contour.Segments.Count);
        Assert.Equal(contour.Segments[0].Start, contour.Segments[^1].End);
        SvgTestSupport.AssertNear(200, SvgTestSupport.SignedArea(contour));
    }

    [Fact]
    public void PolygonIsClosedForFill()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<polygon points=\"10,10 30,10 30,30 10,30\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        Assert.Equal(4, contour.Segments.Count);
        SvgTestSupport.AssertNear(400, SvgTestSupport.SignedArea(contour));
    }

    [Fact]
    public void LineProducesNoFillGeometry()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"10\" />"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" />");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void PolylineWithTwoPointsProducesNoGeometry()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<polyline points=\"10,10 30,10\" />"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" />");

        Assert.Single(artwork.Shapes);
    }

    [Fact]
    public void MultipleShapeElementsKeepDocumentOrder()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" />"
                + "<rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" />");

        Assert.Equal(2, artwork.Shapes.Count);
        SvgTestSupport.AssertNear(0, SvgTestSupport.Bounds(artwork.Shapes[0].Contours).MinX);
        SvgTestSupport.AssertNear(20, SvgTestSupport.Bounds(artwork.Shapes[1].Contours).MinX);
    }

    [Fact]
    public void MissingRectWidthIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><rect height=\"10\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("width", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCircleRadiusIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Contains("'r'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingEllipseRadiusIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><ellipse rx=\"5\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Contains("ry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPolygonPointsIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><polygon /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Contains("points", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPathDataIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><path /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Contains("'d'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeRectExtentIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><rect width=\"-5\" height=\"10\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void NegativeCircleRadiusIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><circle r=\"-5\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void InvalidPointsDataIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><polygon points=\"0,0 10\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("offset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidShapeLengthIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><rect x=\"5em\" width=\"10\" height=\"10\" /></svg>",
            out _);

        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("'x'", error.Message, StringComparison.Ordinal);
    }
}
