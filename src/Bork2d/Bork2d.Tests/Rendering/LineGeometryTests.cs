using System.Numerics;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the CPU line expansion: caps, miter and bevel joins, deduplication,
/// validation, accumulated line UVs, and local bounds.
/// </summary>
public sealed class LineGeometryTests
{
    [Fact]
    public void AnOpenSegmentBuildsFourVerticesAndTwoTriangles()
    {
        LineGeometryData geometry = Build(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.Equal(4, geometry.VertexCount);
        Assert.Equal(6, geometry.IndexCount);
        Assert.Equal(4, geometry.LineUvs.Length);
        Assert.Equal(new uint[] { 0u, 1u, 2u, 1u, 3u, 2u }, geometry.Indices.ToArray());
        Assert.Equal(new Vector2(0f, 1f), geometry.Vertices.Span[0]);
        Assert.Equal(new Vector2(0f, -1f), geometry.Vertices.Span[1]);
        Assert.Equal(new Vector2(10f, 1f), geometry.Vertices.Span[2]);
        Assert.Equal(new Vector2(10f, -1f), geometry.Vertices.Span[3]);
    }

    [Fact]
    public void AnOpenSegmentUsesButtCapsAndHalfTheWidth()
    {
        LineGeometryData geometry = Build(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.Equal(new Vector2(0f, -1f), geometry.Bounds.Min);
        Assert.Equal(new Vector2(10f, 1f), geometry.Bounds.Max);
    }

    [Fact]
    public void LineUvsSpanTheWidthAndAccumulateDistance()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f));

        ReadOnlySpan<Vector2> uvs = geometry.LineUvs.Span;
        Assert.Equal(new Vector2(0f, 0f), uvs[0]);
        Assert.Equal(new Vector2(1f, 0f), uvs[1]);
        Assert.Equal(new Vector2(0f, 10f), uvs[2]);
        Assert.Equal(new Vector2(1f, 10f), uvs[3]);
        Assert.Equal(new Vector2(0f, 10f), uvs[4]);
        Assert.Equal(new Vector2(1f, 10f), uvs[5]);
        Assert.Equal(new Vector2(0f, 20f), uvs[6]);
        Assert.Equal(new Vector2(1f, 20f), uvs[7]);
    }

    [Fact]
    public void AClosedSquareJoinsEveryCornerAndHasNoCaps()
    {
        LineGeometryData geometry = Build(
            [
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
            ],
            closed: true);

        Assert.Equal(16, geometry.VertexCount);
        Assert.Equal(24, geometry.IndexCount);
        Assert.Equal(new Vector2(-1f, -1f), geometry.Bounds.Min);
        Assert.Equal(new Vector2(11f, 11f), geometry.Bounds.Max);
    }

    [Fact]
    public void AClosedSquareAccumulatesLineUvAcrossTheClosingSegment()
    {
        LineGeometryData geometry = Build(
            [
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
            ],
            closed: true);

        ReadOnlySpan<Vector2> uvs = geometry.LineUvs.Span;
        Assert.Equal(new Vector2(0f, 30f), uvs[12]);
        Assert.Equal(new Vector2(1f, 30f), uvs[13]);
        Assert.Equal(new Vector2(0f, 40f), uvs[14]);
        Assert.Equal(new Vector2(1f, 40f), uvs[15]);
    }

    [Fact]
    public void AnLShapedCornerUsesTheOuterMiterPoint()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f));

        Assert.Equal(8, geometry.VertexCount);
        Assert.Equal(12, geometry.IndexCount);
        AssertContains(geometry, new Vector2(11f, -1f));
        AssertContains(geometry, new Vector2(9f, 1f));
        Assert.Equal(new Vector2(0f, -1f), geometry.Bounds.Min);
        Assert.Equal(new Vector2(11f, 10f), geometry.Bounds.Max);
    }

    [Fact]
    public void AMiterBeyondTheLimitFallsBackToABevel()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 1f));

        // Two segment quads plus the bevel triangle at the corner.
        Assert.Equal(11, geometry.VertexCount);
        Assert.Equal(15, geometry.IndexCount);

        // The bevel triangle contains the centerline corner; a miter join would not.
        AssertContains(geometry, new Vector2(10f, 0f));
        Assert.True(
            geometry.Bounds.Max.X < 11f,
            $"A clamped miter leaked into the bounds: {geometry.Bounds.Max.X}.");

        // A left turn puts the bevel on the right width edge (uv x = 1).
        ReadOnlySpan<Vector2> uvs = geometry.LineUvs.Span;
        Assert.Equal(new Vector2(0.5f, 10f), uvs[8]);
        Assert.Equal(new Vector2(1f, 10f), uvs[9]);
        Assert.Equal(new Vector2(1f, 10f), uvs[10]);
    }

    [Fact]
    public void ABevelOnARightTurnUsesTheLeftEdgeUv()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, -1f));

        Assert.Equal(11, geometry.VertexCount);
        Assert.Equal(15, geometry.IndexCount);
        ReadOnlySpan<Vector2> uvs = geometry.LineUvs.Span;
        Assert.Equal(new Vector2(0.5f, 10f), uvs[8]);
        Assert.Equal(new Vector2(0f, 10f), uvs[9]);
        Assert.Equal(new Vector2(0f, 10f), uvs[10]);
    }

    [Fact]
    public void DuplicateConsecutivePointsAreSkipped()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 0f));

        Assert.Equal(4, geometry.VertexCount);
        Assert.Equal(6, geometry.IndexCount);
        Assert.Equal(new Vector2(0f, -1f), geometry.Bounds.Min);
        Assert.Equal(new Vector2(10f, 1f), geometry.Bounds.Max);
    }

    [Fact]
    public void AClosedLineDropsARepeatedFinalPoint()
    {
        LineGeometryData geometry = Build(
            [
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
                new Vector2(0f, 0f),
            ],
            closed: true);

        Assert.Equal(16, geometry.VertexCount);
        Assert.Equal(24, geometry.IndexCount);
        Assert.Equal(new Vector2(-1f, -1f), geometry.Bounds.Min);
        Assert.Equal(new Vector2(11f, 11f), geometry.Bounds.Max);
    }

    [Fact]
    public void ZeroLengthAndOneDistinctPointInputsProduceNoGeometry()
    {
        Assert.Null(LineGeometryBuilder.Build([], closed: false, width: 2f));
        Assert.Null(LineGeometryBuilder.Build([new Vector2(1f, 2f)], closed: false, width: 2f));
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(1f, 2f), new Vector2(1f, 2f), new Vector2(1f, 2f)],
                closed: false,
                width: 2f));
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(1f, 2f), new Vector2(1f, 2f)],
                closed: true,
                width: 2f));
    }

    [Fact]
    public void TooFewDistinctPointsProduceNoGeometry()
    {
        Assert.Null(LineGeometryBuilder.Build([], closed: true, width: 2f));
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(0f, 0f), new Vector2(1f, 0f)],
                closed: true,
                width: 2f));
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f)],
                closed: true,
                width: 2f));
    }

    [Theory]
    [MemberData(nameof(NonFinitePoints))]
    public void ANonFinitePointProducesNoGeometry(Vector2 point)
    {
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(0f, 0f), point, new Vector2(1f, 1f)],
                closed: false,
                width: 2f));
    }

    public static TheoryData<Vector2> NonFinitePoints()
        => new()
        {
            new Vector2(float.NaN, 0f),
            new Vector2(0f, float.NaN),
            new Vector2(float.PositiveInfinity, 0f),
            new Vector2(0f, float.NegativeInfinity),
        };

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void AnInvalidWidthProducesNoGeometry(float width)
    {
        Assert.Null(
            LineGeometryBuilder.Build(
                [new Vector2(0f, 0f), new Vector2(10f, 0f)],
                closed: false,
                width: width));
    }

    [Fact]
    public void BoundsCoverEveryGeneratedVertex()
    {
        LineGeometryData geometry = Build(
            new Vector2(-3f, 2f),
            new Vector2(4f, 5f),
            new Vector2(8f, -1f));

        Bounds bounds = geometry.Bounds;
        Assert.False(bounds.IsEmpty);
        foreach (Vector2 vertex in geometry.Vertices.Span)
        {
            Assert.True(
                bounds.Contains(vertex),
                $"Vertex {vertex} is outside {bounds}.");
        }
    }

    [Fact]
    public void ASelfIntersectingPolylineIsExpandedWithoutSpecialCasing()
    {
        LineGeometryData geometry = Build(
            new Vector2(0f, 0f),
            new Vector2(10f, 10f),
            new Vector2(0f, 10f),
            new Vector2(10f, 0f));

        Assert.Equal(12, geometry.VertexCount);
        Assert.Equal(18, geometry.IndexCount);
    }

    private static LineGeometryData Build(params Vector2[] points)
        => Build(points, closed: false, width: 2f);

    private static LineGeometryData Build(
        Vector2[] points,
        bool closed,
        float width = 2f)
        => Assert.IsType<LineGeometryData>(LineGeometryBuilder.Build(points, closed, width));

    private static void AssertContains(LineGeometryData geometry, Vector2 expected)
    {
        var found = false;
        foreach (Vector2 vertex in geometry.Vertices.Span)
        {
            if (vertex == expected)
            {
                found = true;
                break;
            }
        }

        Assert.True(found, $"Expected vertex {expected} in the expanded geometry.");
    }
}
