using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Shared assertions and parsing helpers for the SVG geometry tests.
/// </summary>
internal static class SvgTestSupport
{
    internal const double Tolerance = 1e-6;

    internal static string Document(string body, string rootAttributes = "viewBox=\"0 0 100 100\"")
        => $"<svg xmlns=\"http://www.w3.org/2000/svg\" {rootAttributes}>{body}</svg>";

    internal static SvgArtwork Parse(string markup)
    {
        (SvgArtwork? artwork, SvgDiagnostics diagnostics) = ParseWithDiagnostics(markup);
        Assert.NotNull(artwork);
        Assert.Empty(diagnostics.Items);
        return artwork!;
    }

    internal static SvgArtwork ParseDocument(string body) => Parse(Document(body));

    internal static (SvgArtwork? Artwork, SvgDiagnostics Diagnostics) ParseWithDiagnostics(string markup)
    {
        var diagnostics = new SvgDiagnostics();
        SvgArtwork? artwork = SvgParser.TryParse(markup, "test.svg", diagnostics);
        return (artwork, diagnostics);
    }

    internal static SvgDiagnostics ParseError(string markup, out SvgArtwork? artwork)
    {
        (SvgArtwork? parsed, SvgDiagnostics diagnostics) = ParseWithDiagnostics(markup);
        artwork = parsed;
        Assert.Null(parsed);
        Assert.True(diagnostics.HasErrors);
        return diagnostics;
    }

    internal static SvgDiagnostic SingleError(SvgDiagnostics diagnostics)
    {
        SvgDiagnostic error = default;
        bool found = false;
        foreach (SvgDiagnostic diagnostic in diagnostics.Items)
        {
            if (diagnostic.Severity != SvgDiagnosticSeverity.Error)
            {
                continue;
            }

            Assert.False(found, "Expected exactly one error diagnostic.");
            error = diagnostic;
            found = true;
        }

        Assert.True(found, "Expected one error diagnostic.");
        return error;
    }

    internal static IReadOnlyList<SvgPoint> Polyline(SvgContour contour) => contour.ToPolyline(1e-4);

    internal static double SignedArea(SvgContour contour) => PolygonArea(Polyline(contour));

    internal static double PolygonArea(IReadOnlyList<SvgPoint> points)
    {
        double sum = 0;
        for (int index = 0; index < points.Count; index++)
        {
            SvgPoint current = points[index];
            SvgPoint next = points[(index + 1) % points.Count];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        return sum / 2;
    }

    internal static SvgBounds Bounds(IEnumerable<SvgContour> contours)
    {
        SvgBounds bounds = SvgBounds.Empty;
        foreach (SvgContour contour in contours)
        {
            foreach (SvgPoint point in Polyline(contour))
            {
                bounds.Include(point);
            }
        }

        return bounds;
    }

    internal static SvgBounds Bounds(SvgContour contour) => Bounds([contour]);

    internal static SvgShape SingleShape(SvgArtwork artwork)
    {
        Assert.Single(artwork.Shapes);
        return artwork.Shapes[0];
    }

    internal static void AssertNear(double expected, double actual, double tolerance = Tolerance)
        => Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} (+/- {tolerance}) but was {actual}.");

    internal static void AssertPoint(SvgPoint actual, double x, double y, double tolerance = Tolerance)
    {
        AssertNear(x, actual.X, tolerance);
        AssertNear(y, actual.Y, tolerance);
    }
}
