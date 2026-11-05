using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The SVG path-data parser: every command, absolute and relative forms, implicit repetition, smooth
/// reflection, arcs, close/reopen behavior, and malformed-data diagnostics.
/// </summary>
public sealed class SvgPathParserTests
{
    [Fact]
    public void MoveLineAndCloseProduceOneClosedContour()
    {
        List<SvgContour> contours = Parse("M 10 10 L 20 10 L 20 20 Z");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.Count);
        Assert.All(contour.Segments, segment => Assert.IsType<SvgLineSegment>(segment));
        SvgTestSupport.AssertPoint(contour.Segments[0].Start, 10, 10);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 20, 10);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 20, 20);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 10, 10);
    }

    [Fact]
    public void RelativeCommandsAreRelativeToTheCurrentPoint()
    {
        List<SvgContour> contours = Parse("m 10 10 l 10 0 l 0 10 z");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.Count);
        SvgTestSupport.AssertPoint(contour.Segments[0].Start, 10, 10);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 20, 10);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 20, 20);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 10, 10);
    }

    [Fact]
    public void RelativeFormsProduceTheSameGeometryAsAbsoluteForms()
    {
        const string absolutePath =
            "M 10 10 L 30 20 H 40 V 30 C 45 35 50 40 55 45 S 60 55 65 60 "
                + "Q 70 65 75 70 T 85 80 A 5 5 0 0 1 90 90 Z";
        const string relativePath =
            "m 10 10 l 20 10 h 10 v 10 c 5 5 10 10 15 15 s 5 10 10 15 "
                + "q 5 5 10 10 t 10 10 a 5 5 0 0 1 5 10 z";

        SvgContour absolute = Assert.Single(Parse(absolutePath));
        SvgContour relative = Assert.Single(Parse(relativePath));

        Assert.Equal(absolute.Segments.Count, relative.Segments.Count);
        IReadOnlyList<SvgPoint> absolutePoints = absolute.ToPolyline(1e-4);
        IReadOnlyList<SvgPoint> relativePoints = relative.ToPolyline(1e-4);
        Assert.Equal(absolutePoints.Count, relativePoints.Count);
        for (int index = 0; index < absolutePoints.Count; index++)
        {
            SvgTestSupport.AssertPoint(
                relativePoints[index],
                absolutePoints[index].X,
                absolutePoints[index].Y,
                1e-9);
        }
    }

    [Fact]
    public void HorizontalAndVerticalCommandsDrawLines()
    {
        List<SvgContour> contours = Parse("M 5 5 H 15 V 20 H 5 V 5");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(4, contour.Segments.Count);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 15, 5);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 15, 20);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 5, 20);
        SvgTestSupport.AssertPoint(contour.Segments[3].End, 5, 5);
    }

    [Fact]
    public void RepeatedCoordinateSetsRepeatTheCommand()
    {
        List<SvgContour> contours = Parse("M 0 0 L 10 0 20 0 30 0");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(4, contour.Segments.Count);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 10, 0);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 20, 0);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 30, 0);
        SvgTestSupport.AssertPoint(contour.Segments[3].End, 0, 0);
    }

    [Fact]
    public void ExtraMoveCoordinatesAreImplicitLineTos()
    {
        List<SvgContour> contours = Parse("M 0 0 10 10 20 20");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.Count);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 10, 10);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 20, 20);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 0, 0);
    }

    [Fact]
    public void RelativeExtraMoveCoordinatesAreImplicitRelativeLineTos()
    {
        List<SvgContour> contours = Parse("m 0 0 10 10 10 0");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.Count);
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 10, 10);
        SvgTestSupport.AssertPoint(contour.Segments[1].End, 20, 10);
        SvgTestSupport.AssertPoint(contour.Segments[2].End, 0, 0);
    }

    [Fact]
    public void CubicControlPointsAndImplicitRepetitionArePreserved()
    {
        List<SvgContour> contours = Parse("M 0 0 C 10 0 20 10 20 20 30 30 40 40 50 50");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.Count);

        SvgCubicSegment first = Assert.IsType<SvgCubicSegment>(contour.Segments[0]);
        SvgTestSupport.AssertPoint(first.Control1, 10, 0);
        SvgTestSupport.AssertPoint(first.Control2, 20, 10);
        SvgTestSupport.AssertPoint(first.End, 20, 20);

        SvgCubicSegment second = Assert.IsType<SvgCubicSegment>(contour.Segments[1]);
        SvgTestSupport.AssertPoint(second.Control1, 30, 30);
        SvgTestSupport.AssertPoint(second.Control2, 40, 40);
        SvgTestSupport.AssertPoint(second.End, 50, 50);
    }

    [Fact]
    public void RelativeCubicUsesTheCurrentPointForEveryControl()
    {
        List<SvgContour> contours = Parse("M 10 10 c 10 0 20 10 20 20");

        SvgCubicSegment cubic = Assert.IsType<SvgCubicSegment>(Assert.Single(contours).Segments[0]);
        SvgTestSupport.AssertPoint(cubic.Start, 10, 10);
        SvgTestSupport.AssertPoint(cubic.Control1, 20, 10);
        SvgTestSupport.AssertPoint(cubic.Control2, 30, 20);
        SvgTestSupport.AssertPoint(cubic.End, 30, 30);
    }

    [Fact]
    public void SmoothCubicReflectsThePreviousControlPoint()
    {
        List<SvgContour> contours = Parse("M 0 0 C 10 0 20 10 20 20 S 30 40 40 40");

        SvgCubicSegment smooth = Assert.IsType<SvgCubicSegment>(Assert.Single(contours).Segments[1]);
        SvgTestSupport.AssertPoint(smooth.Start, 20, 20);
        SvgTestSupport.AssertPoint(smooth.Control1, 20, 30);
        SvgTestSupport.AssertPoint(smooth.Control2, 30, 40);
        SvgTestSupport.AssertPoint(smooth.End, 40, 40);
    }

    [Fact]
    public void SmoothCubicRepeatedSetsReflectEachPreviousSegment()
    {
        List<SvgContour> contours = Parse("M 0 0 C 0 10 10 10 10 0 S 20 -10 20 0 30 10 30 0");

        SvgContour contour = Assert.Single(contours);
        SvgCubicSegment second = Assert.IsType<SvgCubicSegment>(contour.Segments[1]);
        SvgTestSupport.AssertPoint(second.Control1, 10, -10);

        SvgCubicSegment third = Assert.IsType<SvgCubicSegment>(contour.Segments[2]);
        SvgTestSupport.AssertPoint(third.Control1, 20, 10);
    }

    [Fact]
    public void SmoothCubicWithoutAPreviousCurveUsesTheCurrentPoint()
    {
        List<SvgContour> contours = Parse("M 0 0 S 10 10 20 20");

        SvgCubicSegment smooth = Assert.IsType<SvgCubicSegment>(Assert.Single(contours).Segments[0]);
        SvgTestSupport.AssertPoint(smooth.Control1, 0, 0);
    }

    [Fact]
    public void QuadraticAndSmoothReflectionArePreserved()
    {
        List<SvgContour> contours = Parse("M 0 0 Q 10 10 20 0 T 40 0");

        SvgContour contour = Assert.Single(contours);
        SvgQuadraticSegment first = Assert.IsType<SvgQuadraticSegment>(contour.Segments[0]);
        SvgTestSupport.AssertPoint(first.Control, 10, 10);
        SvgTestSupport.AssertPoint(first.End, 20, 0);

        SvgQuadraticSegment second = Assert.IsType<SvgQuadraticSegment>(contour.Segments[1]);
        SvgTestSupport.AssertPoint(second.Control, 30, -10);
        SvgTestSupport.AssertPoint(second.End, 40, 0);
    }

    [Fact]
    public void SmoothQuadraticWithoutAQuadraticUsesTheCurrentPoint()
    {
        List<SvgContour> contours = Parse("M 5 5 T 15 15");

        SvgQuadraticSegment smooth = Assert.IsType<SvgQuadraticSegment>(Assert.Single(contours).Segments[0]);
        SvgTestSupport.AssertPoint(smooth.Control, 5, 5);
    }

    [Fact]
    public void SmoothReflectionDoesNotCrossACloseCommand()
    {
        List<SvgContour> contours = Parse("M 0 0 C 0 10 10 10 10 0 Z M 20 20 T 30 20");

        SvgQuadraticSegment quadratic =
            Assert.IsType<SvgQuadraticSegment>(contours[1].Segments[0]);
        SvgTestSupport.AssertPoint(quadratic.Control, 20, 20);
    }

    [Fact]
    public void QuarterArcBecomesOneCubic()
    {
        List<SvgContour> contours = Parse("M 0 0 A 10 10 0 0 0 10 10");

        SvgContour contour = Assert.Single(contours);
        SvgCubicSegment cubic = Assert.Single(contour.Segments.OfType<SvgCubicSegment>());
        SvgTestSupport.AssertPoint(cubic.Start, 0, 0);
        SvgTestSupport.AssertPoint(cubic.End, 10, 10);

        // The quarter arc's midpoint is (10 - 10*cos(45), 10*sin(45)) in this sweep.
        SvgTestSupport.AssertPoint(cubic.PointAt(0.5), 2.929, 7.071, 0.05);
    }

    [Fact]
    public void HalfArcBecomesTwoCubics()
    {
        List<SvgContour> contours = Parse("M 0 0 A 10 10 0 0 1 20 0");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(2, contour.Segments.OfType<SvgCubicSegment>().Count());
        Assert.Single(contour.Segments.OfType<SvgLineSegment>());
        SvgTestSupport.AssertPoint(contour.Segments[0].Start, 0, 0);
        SvgTestSupport.AssertPoint(contour.Segments[^1].End, 0, 0);
    }

    [Fact]
    public void LargeArcFlagProducesThreeCubics()
    {
        List<SvgContour> contours = Parse("M 0 0 A 10 10 0 1 0 10 10");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(3, contour.Segments.OfType<SvgCubicSegment>().Count());
        SvgTestSupport.AssertPoint(contour.Segments[^1].End, 0, 0);
    }

    [Fact]
    public void ZeroRadiusArcBecomesALine()
    {
        List<SvgContour> contours = Parse("M 0 0 A 0 10 0 0 1 10 10");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(2, contour.Segments.Count);
        Assert.All(contour.Segments, segment => Assert.IsType<SvgLineSegment>(segment));
        SvgTestSupport.AssertPoint(contour.Segments[0].End, 10, 10);
    }

    [Fact]
    public void ArcWithIdenticalEndpointsProducesNoSegment()
    {
        List<SvgContour> contours = Parse("M 5 5 A 10 10 0 1 1 5 5 Z");

        Assert.Empty(contours);
    }

    [Fact]
    public void ArcFlagsMayAbutTheCoordinates()
    {
        List<SvgContour> contours = Parse("M0 0A10 10 0 0110 10");

        SvgContour contour = Assert.Single(contours);
        SvgCubicSegment cubic = Assert.Single(contour.Segments.OfType<SvgCubicSegment>());
        SvgTestSupport.AssertPoint(cubic.End, 10, 10);
    }

    [Fact]
    public void NegativeArcRadiiUseTheirAbsoluteValue()
    {
        List<SvgContour> contours = Parse("M 0 0 A -10 -10 0 0 1 20 0");

        SvgContour contour = Assert.Single(contours);
        Assert.Equal(2, contour.Segments.OfType<SvgCubicSegment>().Count());
    }

    [Fact]
    public void CloseAndReopenStartsANewSubpathAtTheClosedStart()
    {
        List<SvgContour> contours = Parse("M 0 0 L 10 0 L 10 10 Z L 0 10");

        Assert.Equal(2, contours.Count);
        Assert.Equal(3, contours[0].Segments.Count);
        Assert.Equal(2, contours[1].Segments.Count);
        SvgTestSupport.AssertPoint(contours[1].Segments[0].Start, 0, 0);
        SvgTestSupport.AssertPoint(contours[1].Segments[0].End, 0, 10);
        SvgTestSupport.AssertPoint(contours[1].Segments[1].End, 0, 0);
    }

    [Fact]
    public void RelativeMoveAfterCloseIsRelativeToTheClosedStart()
    {
        List<SvgContour> contours = Parse("M 10 10 L 20 10 Z m 5 5 l 5 0");

        Assert.Equal(2, contours.Count);
        SvgTestSupport.AssertPoint(contours[1].Segments[0].Start, 15, 15);
        SvgTestSupport.AssertPoint(contours[1].Segments[0].End, 20, 15);
    }

    [Fact]
    public void MultipleMoveTosProduceMultipleContours()
    {
        List<SvgContour> contours = Parse("M 0 0 L 10 0 Z M 20 20 L 30 20 Z");

        Assert.Equal(2, contours.Count);
        SvgTestSupport.AssertPoint(contours[1].Segments[0].Start, 20, 20);
    }

    [Fact]
    public void RepeatedCloseCommandsAreHarmless()
    {
        List<SvgContour> contours = Parse("M 0 0 L 10 0 Z Z");

        Assert.Single(contours);
    }

    [Fact]
    public void EmptyPathDataProducesNoContours()
    {
        Assert.Empty(Parse(string.Empty));
        Assert.Empty(Parse("   "));
    }

    [Fact]
    public void PathDataMustBeginWithAMoveto()
    {
        Assert.Contains("moveto", ParseFailure("L 10 10"), StringComparison.Ordinal);
        Assert.Contains("moveto", ParseFailure("Z"), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingCoordinatesAreAnError()
    {
        Assert.Contains("offset", ParseFailure("M 10"), StringComparison.Ordinal);
        Assert.Contains("offset", ParseFailure("M 0 0 L 10"), StringComparison.Ordinal);
        Assert.Contains("offset", ParseFailure("M 0 0 C 1 2 3"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandIsAnError()
    {
        Assert.Contains("character offset", ParseFailure("M 0 0 X 1 2"), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidArcFlagIsAnError()
    {
        string message = ParseFailure("M 0 0 A 10 10 0 2 1 20 20");

        Assert.Contains("flag", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteNumberIsAnError()
    {
        string message = ParseFailure("M 0 0 L 1e999 0");

        Assert.Contains("not finite", message, StringComparison.Ordinal);
    }

    private static List<SvgContour> Parse(string data)
    {
        bool parsed = SvgPathParser.TryParse(data, out List<SvgContour> contours, out string message);
        Assert.True(parsed, message);
        Assert.Empty(message);
        return contours;
    }

    private static string ParseFailure(string data)
    {
        bool parsed = SvgPathParser.TryParse(data, out List<SvgContour> contours, out string message);

        Assert.False(parsed);
        Assert.Empty(contours);
        Assert.NotEmpty(message);
        return message;
    }
}
