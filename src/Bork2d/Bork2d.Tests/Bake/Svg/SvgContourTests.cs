using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// The contour model: flattening, reversal, transformation, and the closed-chain invariant.
/// </summary>
public sealed class SvgContourTests
{
    [Fact]
    public void ToPolylineFlattensCurvesAndOmitsTheClosingVertex()
    {
        var contour = new SvgContour(
        [
            new SvgCubicSegment(
                new SvgPoint(0, 0),
                new SvgPoint(0, 10),
                new SvgPoint(10, 10),
                new SvgPoint(10, 0)),
            new SvgLineSegment(new SvgPoint(10, 0), new SvgPoint(0, 0)),
        ]);

        IReadOnlyList<SvgPoint> points = contour.ToPolyline(0.01);

        Assert.True(points.Count > 3);
        SvgTestSupport.AssertPoint(points[0], 0, 0);
        SvgTestSupport.AssertPoint(points[^1], 10, 0);
        Assert.NotEqual(points[0], points[^1]);
    }

    [Fact]
    public void ToPolylineUsesFewerPointsForACoarserTolerance()
    {
        var contour = new SvgContour(
        [
            new SvgCubicSegment(
                new SvgPoint(0, 0),
                new SvgPoint(0, 10),
                new SvgPoint(10, 10),
                new SvgPoint(10, 0)),
            new SvgLineSegment(new SvgPoint(10, 0), new SvgPoint(0, 0)),
        ]);

        int fine = contour.ToPolyline(0.001).Count;
        int coarse = contour.ToPolyline(1).Count;

        Assert.True(fine > coarse);
        Assert.True(coarse >= 3);
    }

    [Fact]
    public void ToPolylineRejectsAnInvalidTolerance()
    {
        SvgContour contour = Triangle();

        Assert.Throws<ArgumentOutOfRangeException>(() => contour.ToPolyline(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => contour.ToPolyline(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => contour.ToPolyline(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => contour.ToPolyline(double.PositiveInfinity));
    }

    [Fact]
    public void ReverseNegatesTheSignedAreaAndKeepsTheClosure()
    {
        SvgContour contour = Triangle();
        double area = SvgTestSupport.SignedArea(contour);

        SvgContour reversed = contour.Reverse();

        SvgTestSupport.AssertNear(-area, SvgTestSupport.SignedArea(reversed));
        SvgTestSupport.AssertPoint(reversed.Segments[0].Start, 0, 0);
        SvgTestSupport.AssertPoint(reversed.Segments[^1].End, 0, 0);
        Assert.Equal(3, reversed.Segments.Count);
    }

    [Fact]
    public void ReverseTwiceRestoresTheOriginalGeometry()
    {
        SvgContour contour = Triangle();
        double area = SvgTestSupport.SignedArea(contour);

        SvgContour twice = contour.Reverse().Reverse();

        SvgTestSupport.AssertNear(area, SvgTestSupport.SignedArea(twice));
        SvgTestSupport.AssertPoint(twice.Segments[0].Start, 0, 0);
        SvgTestSupport.AssertPoint(twice.Segments[0].End, 10, 0);
    }

    [Fact]
    public void TransformMapsEveryPoint()
    {
        SvgContour contour = Triangle();

        SvgContour moved = contour.Transform(SvgAffine.Translate(5, 7));

        SvgBounds bounds = SvgTestSupport.Bounds(moved);
        SvgTestSupport.AssertNear(5, bounds.MinX);
        SvgTestSupport.AssertNear(15, bounds.MaxX);
        SvgTestSupport.AssertNear(7, bounds.MinY);
        SvgTestSupport.AssertNear(17, bounds.MaxY);
        SvgTestSupport.AssertNear(
            SvgTestSupport.SignedArea(contour),
            SvgTestSupport.SignedArea(moved));
    }

    [Fact]
    public void ConstructorRejectsAnOpenChain()
    {
        Assert.Throws<ArgumentException>(
            () => new SvgContour([new SvgLineSegment(new SvgPoint(0, 0), new SvgPoint(10, 0))]));
    }

    [Fact]
    public void ConstructorRejectsABrokenChain()
    {
        Assert.Throws<ArgumentException>(
            () => new SvgContour(
            [
                new SvgLineSegment(new SvgPoint(0, 0), new SvgPoint(10, 0)),
                new SvgLineSegment(new SvgPoint(20, 0), new SvgPoint(0, 0)),
            ]));
    }

    [Fact]
    public void ConstructorRejectsAnEmptyChain()
    {
        Assert.Throws<ArgumentException>(() => new SvgContour([]));
    }

    private static SvgContour Triangle()
        => new(
        [
            new SvgLineSegment(new SvgPoint(0, 0), new SvgPoint(10, 0)),
            new SvgLineSegment(new SvgPoint(10, 0), new SvgPoint(0, 10)),
            new SvgLineSegment(new SvgPoint(0, 10), new SvgPoint(0, 0)),
        ]);
}
