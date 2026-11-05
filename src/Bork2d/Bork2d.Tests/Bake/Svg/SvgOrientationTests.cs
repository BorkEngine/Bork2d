using Bork2d.Bake.Svg;
using Xunit;

namespace Bork2d.Tests.Bake.Svg;

/// <summary>
/// Orientation normalization: nonzero per-component fixing, even-odd nesting depth, holes, multiple
/// subpaths, and the documented overlap limitation.
/// </summary>
public sealed class SvgOrientationTests
{
    [Fact]
    public void SingleContourIsNormalizedToPositiveArea()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" />"));

        SvgTestSupport.AssertNear(2500, SvgTestSupport.SignedArea(Assert.Single(shape.Contours)));
    }

    [Fact]
    public void NonZeroPreservesWindingAcrossHolesWhenTheOuterIsNegative()
    {
        // The outer contour winds clockwise on screen (positive in SVG's Y-down shoelace), so the
        // Y-up flip makes it negative and the whole shape must be reversed.
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 100 0 L 100 100 L 0 100 Z "
                + "M 25 25 L 25 75 L 75 75 L 75 25 Z\" />"));

        Assert.Equal(2, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(-2500, SvgTestSupport.SignedArea(shape.Contours[1]));
    }

    [Fact]
    public void NonZeroKeepsWindingWhenTheOuterIsAlreadyPositive()
    {
        // The outer contour winds counter-clockwise on screen, so the Y-up flip already makes it
        // positive and no reversal is needed.
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 0 100 L 100 100 L 100 0 Z "
                + "M 25 25 L 75 25 L 75 75 L 25 75 Z\" />"));

        Assert.Equal(2, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(-2500, SvgTestSupport.SignedArea(shape.Contours[1]));
    }

    [Fact]
    public void NonZeroMakesAnIsolatedNegativeContourPositive()
    {
        // The second subpath is disjoint from the first and winds clockwise on screen, so the Y-up
        // flip leaves it negative. Nonzero fill still fills it, so it must become a positive root.
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 0 100 L 100 100 L 100 0 Z "
                + "M 150 0 L 170 0 L 170 20 L 150 20 Z\" />"));

        Assert.Equal(SvgFillRule.NonZero, shape.FillRule);
        Assert.Equal(2, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(400, SvgTestSupport.SignedArea(shape.Contours[1]));
    }

    [Fact]
    public void NonZeroFlipsANegativeRootAndItsPositiveHole()
    {
        // The outer winds clockwise on screen (negative after the Y-up flip) and the hole winds
        // counter-clockwise (positive after the flip). Reversing the whole component must keep the
        // hole opposite to the outer instead of losing it.
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 100 0 L 100 100 L 0 100 Z "
                + "M 30 30 L 30 70 L 70 70 L 70 30 Z\" />"));

        Assert.Equal(2, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(-1600, SvgTestSupport.SignedArea(shape.Contours[1]));
    }

    [Fact]
    public void NonZeroFlipsANegativeRootAndEveryDescendant()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 100 0 L 100 100 L 0 100 Z "
                + "M 20 20 L 20 80 L 80 80 L 80 20 Z "
                + "M 40 40 L 60 40 L 60 60 L 40 60 Z\" />"));

        Assert.Equal(3, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(-3600, SvgTestSupport.SignedArea(shape.Contours[1]));
        SvgTestSupport.AssertNear(400, SvgTestSupport.SignedArea(shape.Contours[2]));
    }

    [Fact]
    public void EvenOddOrientsNestedRingsByDepth()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path fill-rule=\"evenodd\" d=\"M 0 0 H 100 V 100 H 0 Z "
                + "M 20 20 H 80 V 80 H 20 Z M 40 40 H 60 V 60 H 40 Z\" />"));

        Assert.Equal(SvgFillRule.EvenOdd, shape.FillRule);
        Assert.Equal(3, shape.Contours.Count);
        SvgTestSupport.AssertNear(10000, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(-3600, SvgTestSupport.SignedArea(shape.Contours[1]));
        SvgTestSupport.AssertNear(400, SvgTestSupport.SignedArea(shape.Contours[2]));
    }

    [Fact]
    public void EvenOddHoleAreaIsSubtractedFromTheShape()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path fill-rule=\"evenodd\" d=\"M 0 0 H 100 V 100 H 0 Z "
                + "M 25 25 H 75 V 75 H 25 Z\" />"));

        double total = 0;
        foreach (SvgContour contour in shape.Contours)
        {
            total += SvgTestSupport.SignedArea(contour);
        }

        SvgTestSupport.AssertNear(7500, total);
    }

    [Fact]
    public void EvenOddIndependentContoursAreBothPositive()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path fill-rule=\"evenodd\" d=\"M 0 0 H 10 V 10 H 0 Z M 20 20 H 30 V 30 H 20 Z\" />"));

        Assert.Equal(2, shape.Contours.Count);
        SvgTestSupport.AssertNear(100, SvgTestSupport.SignedArea(shape.Contours[0]));
        SvgTestSupport.AssertNear(100, SvgTestSupport.SignedArea(shape.Contours[1]));
    }

    [Fact]
    public void EvenOddNestedHoleIsNegativeEvenWhenItUsesTheOuterWinding()
    {
        // Both subpaths wind clockwise on screen; even-odd still makes the inner one a hole.
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path fill-rule=\"evenodd\" d=\"M 0 0 H 100 V 100 H 0 Z "
                + "M 25 25 H 75 V 75 H 25 Z\" />"));

        Assert.True(SvgTestSupport.SignedArea(shape.Contours[0]) > 0);
        Assert.True(SvgTestSupport.SignedArea(shape.Contours[1]) < 0);
    }

    [Fact]
    public void MultipleSubpathsInOnePathShareOneShape()
    {
        SvgArtwork artwork = SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 H 100 V 100 H 0 Z M 25 25 H 75 V 75 H 25 Z\" />");

        SvgShape shape = SvgTestSupport.SingleShape(artwork);
        Assert.Equal(2, shape.Contours.Count);
        Assert.Equal(SvgFillRule.NonZero, shape.FillRule);
    }

    [Fact]
    public void OverlappingSubpathsDoNotProduceAnError()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path fill-rule=\"evenodd\" d=\"M 0 0 H 50 V 50 H 0 Z M 10 10 H 60 V 60 H 10 Z\" />"));

        Assert.Equal(2, shape.Contours.Count);
        foreach (SvgContour contour in shape.Contours)
        {
            Assert.True(Math.Abs(SvgTestSupport.SignedArea(contour)) > 0);
        }
    }

    [Fact]
    public void EveryContourIsClosedWithNoDuplicateClosingVertex()
    {
        SvgShape shape = SvgTestSupport.SingleShape(SvgTestSupport.ParseDocument(
            "<path d=\"M 0 0 L 100 0 L 100 100 L 0 100 Z\" />"));

        SvgContour contour = Assert.Single(shape.Contours);
        Assert.Equal(contour.Segments[0].Start, contour.Segments[^1].End);
        IReadOnlyList<SvgPoint> points = contour.ToPolyline(0.01);
        Assert.NotEqual(points[0], points[^1]);
    }
}
