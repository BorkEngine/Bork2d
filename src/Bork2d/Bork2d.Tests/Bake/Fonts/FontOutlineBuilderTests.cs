using Bork2d.Bake.Fonts;
using Season.MSDF;
using StbTrueTypeSharp;
using Xunit;

namespace Bork2d.Tests.Bake.Fonts;

/// <summary>
/// The outline builder over synthetic <c>stbtt_vertex</c> streams: line contours, implicit contour
/// closing, quadratic and cubic segments, empty glyphs, and invalid streams. The synthetic cubic case
/// covers the CFF cubic path without committing an OTF fixture; the actual CFF parser is
/// StbTrueTypeSharp library code.
/// </summary>
public sealed class FontOutlineBuilderTests
{
    private const int UnitsPerEm = 1000;

    [Fact]
    public void LineContourIsClosedAndScaledToEmUnits()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            Line(1000, 0),
            Line(1000, 1000),
        ];

        Assert.True(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out Shape shape, out string? failure),
            failure);

        Contour contour = Assert.Single(shape.Contours);
        Assert.Equal(3, contour.Edges.Count);
        Assert.All(contour.Edges, edge => Assert.IsType<LinearSegment>(edge.EdgeSegment));
        Season.MSDF.Shape.Bounds bounds = shape.GetBounds(0, 1, 0);
        Assert.Equal(0.0, bounds.L, 6);
        Assert.Equal(0.0, bounds.B, 6);
        Assert.Equal(1.0, bounds.R, 6);
        Assert.Equal(1.0, bounds.T, 6);
        Assert.True(shape.Validate());
    }

    [Fact]
    public void LastPointEqualToTheStartDoesNotAddAClosingSegment()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            Line(1000, 0),
            Line(0, 0),
        ];

        Assert.True(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out Shape shape, out string? failure),
            failure);

        Contour contour = Assert.Single(shape.Contours);
        Assert.Equal(2, contour.Edges.Count);
    }

    [Fact]
    public void QuadraticSegmentUsesOneControlPoint()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            Curve(1000, 0, 500, 1000),
        ];

        Assert.True(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out Shape shape, out string? failure),
            failure);

        Contour contour = Assert.Single(shape.Contours);
        Assert.Equal(2, contour.Edges.Count);
        Assert.Single(contour.Edges, edge => edge.EdgeSegment is QuadraticSegment);
        Season.MSDF.Shape.Bounds bounds = shape.GetBounds(0, 1, 0);
        Assert.Equal(0.0, bounds.L, 6);
        Assert.Equal(0.0, bounds.B, 6);
        Assert.Equal(1.0, bounds.R, 6);
        Assert.Equal(0.5, bounds.T, 6);
    }

    [Fact]
    public void CubicSegmentUsesTwoControlPoints()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            Cubic(1000, 0, 250, 1000, 750, 1000),
        ];

        Assert.True(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out Shape shape, out string? failure),
            failure);

        Contour contour = Assert.Single(shape.Contours);
        Assert.Equal(2, contour.Edges.Count);
        Assert.Single(contour.Edges, edge => edge.EdgeSegment is CubicSegment);
        Season.MSDF.Shape.Bounds bounds = shape.GetBounds(0, 1, 0);
        Assert.Equal(0.0, bounds.L, 6);
        Assert.Equal(0.0, bounds.B, 6);
        Assert.Equal(1.0, bounds.R, 6);
        Assert.Equal(0.75, bounds.T, 6);
    }

    [Fact]
    public void TwoContoursStaySeparate()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            Line(500, 0),
            Line(500, 500),
            Move(600, 600),
            Line(1000, 600),
            Line(1000, 1000),
        ];

        Assert.True(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out Shape shape, out string? failure),
            failure);

        Assert.Equal(2, shape.Contours.Count);
        Assert.Equal(6, shape.EdgeCount());
    }

    [Fact]
    public void EmptyVertexStreamProducesAnEmptyShape()
    {
        Assert.True(
            FontOutlineBuilder.TryBuild(
                ReadOnlySpan<StbTrueType.stbtt_vertex>.Empty,
                UnitsPerEm,
                out Shape shape,
                out string? failure),
            failure);

        Assert.Empty(shape.Contours);
    }

    [Fact]
    public void SegmentBeforeTheFirstMoveFails()
    {
        StbTrueType.stbtt_vertex[] vertices = [Line(1000, 0)];

        Assert.False(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out _, out string? failure));
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    [Fact]
    public void UnsupportedVertexTypeFails()
    {
        StbTrueType.stbtt_vertex[] vertices =
        [
            Move(0, 0),
            new() { type = 9, x = 100, y = 100 },
        ];

        Assert.False(
            FontOutlineBuilder.TryBuild(vertices, UnitsPerEm, out _, out string? failure));
        Assert.Contains("9", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveUnitsPerEmFails()
    {
        StbTrueType.stbtt_vertex[] vertices = [Move(0, 0), Line(1000, 0)];

        Assert.False(
            FontOutlineBuilder.TryBuild(vertices, 0, out _, out string? failure));
        Assert.False(string.IsNullOrWhiteSpace(failure));
    }

    private static StbTrueType.stbtt_vertex Move(short x, short y)
        => new() { type = FontOutlineBuilder.VertexMove, x = x, y = y };

    private static StbTrueType.stbtt_vertex Line(short x, short y)
        => new() { type = FontOutlineBuilder.VertexLine, x = x, y = y };

    private static StbTrueType.stbtt_vertex Curve(short x, short y, short controlX, short controlY)
        => new()
        {
            type = FontOutlineBuilder.VertexQuadratic,
            x = x,
            y = y,
            cx = controlX,
            cy = controlY,
        };

    private static StbTrueType.stbtt_vertex Cubic(
        short x,
        short y,
        short controlX,
        short controlY,
        short control1X,
        short control1Y)
        => new()
        {
            type = FontOutlineBuilder.VertexCubic,
            x = x,
            y = y,
            cx = controlX,
            cy = controlY,
            cx1 = control1X,
            cy1 = control1Y,
        };
}
