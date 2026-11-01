using System.Numerics;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the public <see cref="Bounds"/> world-space AABB: empty semantics,
/// center/size, containment, intersection (including touching edges), and default/invalid values.
/// </summary>
public sealed class BoundsTests
{
    [Fact]
    public void DefaultValueIsTheEmptySentinel()
    {
        Bounds empty = default;

        Assert.True(empty.IsEmpty);
        Assert.Equal(Bounds.Empty, empty);
        Assert.Equal(Vector2.Zero, empty.Center);
        Assert.Equal(Vector2.Zero, empty.Size);
    }

    [Fact]
    public void EmptyBoundsContainAndIntersectNothing()
    {
        Bounds unit = new(Vector2.Zero, Vector2.One);

        Assert.False(Bounds.Empty.Contains(Vector2.Zero));
        Assert.False(Bounds.Empty.Contains(new Vector2(0.5f)));
        Assert.False(Bounds.Empty.Intersects(unit));
        Assert.False(unit.Intersects(Bounds.Empty));
        Assert.False(unit.Intersects(default));
    }

    [Fact]
    public void InvertedAndNonFiniteBoxesAreEmpty()
    {
        Bounds invertedX = new(new Vector2(2f, 0f), new Vector2(1f, 1f));
        Bounds invertedY = new(new Vector2(0f, 2f), new Vector2(1f, 1f));
        Bounds notANumber = new(new Vector2(float.NaN, 0f), new Vector2(1f, 1f));
        Bounds infinity = new(new Vector2(0f, 0f), new Vector2(float.PositiveInfinity, 1f));

        Assert.True(invertedX.IsEmpty);
        Assert.True(invertedY.IsEmpty);
        Assert.True(notANumber.IsEmpty);
        Assert.True(infinity.IsEmpty);
        Assert.Equal(Vector2.Zero, invertedX.Center);
        Assert.Equal(Vector2.Zero, invertedX.Size);
        Assert.False(invertedX.Contains(new Vector2(1.5f, 0.5f)));
    }

    [Fact]
    public void ZeroAreaBoxesAreEmpty()
    {
        Bounds flat = new(new Vector2(1f, 2f), new Vector2(1f, 5f));
        Bounds point = new(new Vector2(1f, 2f), new Vector2(1f, 2f));

        Assert.True(flat.IsEmpty);
        Assert.True(point.IsEmpty);
        Assert.Equal(Vector2.Zero, flat.Size);
        Assert.Equal(Vector2.Zero, point.Center);
    }

    [Fact]
    public void CenterAndSizeDescribeTheBox()
    {
        Bounds box = new(new Vector2(1f, 2f), new Vector2(5f, 8f));

        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(3f, 5f), box.Center);
        Assert.Equal(new Vector2(4f, 6f), box.Size);
        Assert.Equal(new Vector2(1f, 2f), box.Min);
        Assert.Equal(new Vector2(5f, 8f), box.Max);
    }

    [Fact]
    public void ContainsIsInclusiveAtTheEdges()
    {
        Bounds box = new(new Vector2(0f, 0f), new Vector2(10f, 5f));

        Assert.True(box.Contains(new Vector2(5f, 2.5f)));
        Assert.True(box.Contains(new Vector2(0f, 0f)));
        Assert.True(box.Contains(new Vector2(10f, 5f)));
        Assert.True(box.Contains(new Vector2(0f, 5f)));
        Assert.False(box.Contains(new Vector2(-0.001f, 0f)));
        Assert.False(box.Contains(new Vector2(10.001f, 5f)));
        Assert.False(box.Contains(new Vector2(float.NaN, 2f)));
    }

    [Fact]
    public void IntersectsIncludesTouchingEdgesAndCorners()
    {
        Bounds box = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
        Bounds overlapping = new(new Vector2(5f, 5f), new Vector2(15f, 15f));
        Bounds contained = new(new Vector2(2f, 2f), new Vector2(3f, 3f));
        Bounds touchingEdge = new(new Vector2(10f, 2f), new Vector2(12f, 4f));
        Bounds touchingCorner = new(new Vector2(10f, 10f), new Vector2(12f, 12f));
        Bounds separate = new(new Vector2(10.001f, 0f), new Vector2(12f, 2f));

        Assert.True(box.Intersects(overlapping));
        Assert.True(overlapping.Intersects(box));
        Assert.True(box.Intersects(contained));
        Assert.True(contained.Intersects(box));
        Assert.True(box.Intersects(touchingEdge));
        Assert.True(box.Intersects(touchingCorner));
        Assert.False(box.Intersects(separate));
        Assert.False(separate.Intersects(box));
    }

    [Fact]
    public void IntersectsIsSymmetricAndEmptySafeForInvalidBoxes()
    {
        Bounds box = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
        Bounds inverted = new(new Vector2(10f, 10f), new Vector2(0f, 0f));

        Assert.False(box.Intersects(inverted));
        Assert.False(inverted.Intersects(box));
    }
}
