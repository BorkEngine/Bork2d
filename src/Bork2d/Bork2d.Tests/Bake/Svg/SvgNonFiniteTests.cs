using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Non-representable geometry: an overflowing transform, relative coordinate arithmetic, or artboard
/// mapping is a <c>BK9202</c> diagnostic with no artwork and no exception.
/// </summary>
public sealed class SvgNonFiniteTests
{
    [Fact]
    public void OverflowingTransformIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\">"
                + "<rect width=\"5\" height=\"5\" transform=\"scale(1e308) scale(1e308)\" /></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Equal(1, error.Line);
        Assert.Contains("non-finite", error.Message, StringComparison.Ordinal);
        Assert.Contains("transform", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowingRelativePathCoordinateIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 0 10 10\"><path d=\"m 1e308 0 l 1e308 0\" /></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("non-finite", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowingArtboardMappingIsBK9202()
    {
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg viewBox=\"0 1e308 10 1e308\"><rect width=\"10\" height=\"10\" /></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Equal(1, error.Line);
        Assert.Contains("artboard", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowingGeometryBoundsAreBK9202()
    {
        // The coordinates are finite, but their span overflows the artboard extent; the parser must
        // report BK9202 instead of handing SvgArtwork a non-finite size.
        SvgDiagnostics diagnostics = SvgTestSupport.ParseError(
            "<svg><path d=\"M -1e308 0 L 1e308 0 L 1e308 1 L -1e308 1 Z\" /></svg>",
            out SvgArtwork? artwork);

        Assert.Null(artwork);
        SvgDiagnostic error = SvgTestSupport.SingleError(diagnostics);
        Assert.Equal(SvgDiagnosticCodes.ContentInvalid, error.Code);
        Assert.Contains("artboard", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeButFiniteGeometryStillParses()
    {
        SvgArtwork artwork = SvgTestSupport.Parse(
            "<svg viewBox=\"0 0 1000 1000\">"
                + "<rect width=\"10\" height=\"10\" transform=\"scale(50)\" /></svg>");

        SvgBounds bounds = SvgTestSupport.Bounds(SvgTestSupport.SingleShape(artwork).Contours);
        SvgTestSupport.AssertNear(500, bounds.MaxX);
        SvgTestSupport.AssertNear(1000, bounds.MaxY);
    }
}
