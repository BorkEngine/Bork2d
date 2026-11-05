using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Artboard resolution and the user-coordinate to artboard-local Y-up mapping: <c>viewBox</c>,
/// <c>width</c>/<c>height</c> fallback, geometry-bounds fallback, root transform ordering, and
/// invalid artboard attributes.
/// </summary>
public sealed class SvgArtboardTests
{
    [Fact]
    public void ViewBoxDefinesTheArtboard()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg viewBox=\"10 20 30 40\">"
                + "<rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" /></svg>");

        SvgTestSupport.AssertNear(30, artwork.Width);
        SvgTestSupport.AssertNear(40, artwork.Height);
        Assert.True(artwork.HasGeometry);

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(0, bounds.MinX);
        SvgTestSupport.AssertNear(0, bounds.MinY);
        SvgTestSupport.AssertNear(30, bounds.MaxX);
        SvgTestSupport.AssertNear(40, bounds.MaxY);
    }

    [Fact]
    public void RootWidthAndHeightDefineTheArtboard()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg width=\"100\" height=\"50\">"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" /></svg>");

        SvgTestSupport.AssertNear(100, artwork.Width);
        SvgTestSupport.AssertNear(50, artwork.Height);

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(0, bounds.MinX);
        SvgTestSupport.AssertNear(40, bounds.MinY);
        SvgTestSupport.AssertNear(10, bounds.MaxX);
        SvgTestSupport.AssertNear(50, bounds.MaxY);
    }

    [Fact]
    public void PxLengthsAreAccepted()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg width=\"100px\" height=\"50px\">"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" /></svg>");

        SvgTestSupport.AssertNear(100, artwork.Width);
        SvgTestSupport.AssertNear(50, artwork.Height);
    }

    [Fact]
    public void GeometryBoundsDefineTheArtboardWhenTheRootHasNoExtent()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg><rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" /></svg>");

        SvgTestSupport.AssertNear(30, artwork.Width);
        SvgTestSupport.AssertNear(40, artwork.Height);

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(0, bounds.MinX);
        SvgTestSupport.AssertNear(0, bounds.MinY);
        SvgTestSupport.AssertNear(30, bounds.MaxX);
        SvgTestSupport.AssertNear(40, bounds.MaxY);
    }

    [Fact]
    public void GeometryBoundsIncludeCurveExtrema()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg><path d=\"M 0 0 C 0 10 10 10 10 0 Z\" /></svg>");

        SvgTestSupport.AssertNear(10, artwork.Width);

        // The cubic's top reaches 7.5, above both endpoints, so the bounds must use the extremum.
        SvgTestSupport.AssertNear(7.5, artwork.Height);
    }

    [Fact]
    public void ViewBoxWinsOverRootWidthAndHeight()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg viewBox=\"0 0 30 40\" width=\"500\" height=\"500\">"
                + "<rect x=\"0\" y=\"0\" width=\"30\" height=\"40\" /></svg>");

        SvgTestSupport.AssertNear(30, artwork.Width);
        SvgTestSupport.AssertNear(40, artwork.Height);
    }

    [Fact]
    public void RootTransformIsAppliedBeforeTheArtboardMapping()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg viewBox=\"0 0 100 100\" transform=\"translate(10,0)\">"
                + "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" /></svg>");

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(10, bounds.MinX);
        SvgTestSupport.AssertNear(90, bounds.MinY);
        SvgTestSupport.AssertNear(20, bounds.MaxX);
        SvgTestSupport.AssertNear(100, bounds.MaxY);
    }

    [Fact]
    public void NegativeViewBoxOriginMapsToTheOrigin()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg viewBox=\"-5 -5 10 10\">"
                + "<rect x=\"-5\" y=\"-5\" width=\"10\" height=\"10\" /></svg>");

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(0, bounds.MinX);
        SvgTestSupport.AssertNear(0, bounds.MinY);
        SvgTestSupport.AssertNear(10, bounds.MaxX);
        SvgTestSupport.AssertNear(10, bounds.MaxY);
    }

    [Fact]
    public void InvalidViewBoxExtentIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 0 10\"><rect width=\"10\" height=\"10\" /></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void InvalidViewBoxNumberCountIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10\"><rect width=\"10\" height=\"10\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void PercentWidthIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg width=\"100%\" height=\"100%\"><rect width=\"10\" height=\"10\" /></svg>",
            out _);

        bool sawWidthError = false;
        foreach (SvgDiagnostic diagnostic in diagnostics.Items)
        {
            Assert.Equal(SvgDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(SvgDiagnosticCodes.ContentInvalid, diagnostic.Code);
            sawWidthError |= diagnostic.Message.Contains("'width'", StringComparison.Ordinal);
        }

        Assert.True(sawWidthError);
    }

    [Fact]
    public void NonPositiveWidthIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg width=\"0\" height=\"10\"><rect width=\"10\" height=\"10\" /></svg>",
            out _);

        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, SvgTestSupport.SingleError(diagnostics).Code);
    }

    [Fact]
    public void WidthWithoutHeightFallsBackToGeometryBounds()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg width=\"500\"><rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" /></svg>");

        SvgTestSupport.AssertNear(30, artwork.Width);
        SvgTestSupport.AssertNear(40, artwork.Height);
    }

    [Fact]
    public void EmptyArtworkIsBK9205()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\" />",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        Assert.Equal(SvgDiagnosticCodes.NoGeometry, SvgTestSupport.SingleError(diagnostics).Code);
    }
}
