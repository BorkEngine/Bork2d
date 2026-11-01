using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Engine.Mathematics;
using Xunit;

namespace Bork2d.Tests.Mathematics;

/// <summary>
/// Deterministic tests for the public <see cref="Bounds"/> 2D AABB: the 16-byte layout, empty
/// semantics, center/size and min/max properties, inclusive containment and intersection, closest
/// point and distance, the mutating helpers, formatting, equality, and ray intersection.
/// </summary>
public sealed class BoundsTests
{
    [Fact]
    public void BoundsIsExactlySixteenBytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<Bounds>());
        Assert.Equal(2 * Unsafe.SizeOf<Vector2>(), Unsafe.SizeOf<Bounds>());
    }

    [Fact]
    public void DefaultValueIsTheEmptySentinel()
    {
        Bounds empty = default;

        Assert.True(empty.IsEmpty);
        Assert.True(Bounds.Empty.IsEmpty);
        Assert.Equal(Bounds.Empty, empty);
        Assert.Equal(Vector2.Zero, empty.Center);
        Assert.Equal(Vector2.Zero, empty.Size);
        Assert.Equal(Vector2.Zero, empty.Extents);
        Assert.Equal(Vector2.Zero, empty.Min);
        Assert.Equal(Vector2.Zero, empty.Max);
    }

    [Fact]
    public void ZeroAreaBoxesAreEmpty()
    {
        Bounds point = Bounds.FromMinMax(new Vector2(1f, 2f), new Vector2(1f, 2f));
        Bounds flat = Bounds.FromMinMax(new Vector2(1f, 2f), new Vector2(5f, 2f));
        Bounds zeroSize = new(new Vector2(3f, 4f), Vector2.Zero);

        Assert.True(point.IsEmpty);
        Assert.True(flat.IsEmpty);
        Assert.True(zeroSize.IsEmpty);
        Assert.Equal(Vector2.Zero, point.Center);
        Assert.Equal(Vector2.Zero, flat.Size);
        Assert.Equal(Vector2.Zero, zeroSize.Extents);
        Assert.Equal(new Vector2(1f, 2f), point.Min);
        Assert.Equal(new Vector2(1f, 2f), point.Max);
        Assert.False(point.Contains(new Vector2(1f, 2f)));
    }

    [Fact]
    public void InvertedAndNonFiniteBoxesAreEmpty()
    {
        Bounds invertedX = Bounds.FromMinMax(new Vector2(2f, 0f), new Vector2(1f, 1f));
        Bounds invertedY = Bounds.FromMinMax(new Vector2(0f, 2f), new Vector2(1f, 1f));
        Bounds negativeSize = new(new Vector2(0f, 0f), new Vector2(-2f, 1f));
        Bounds notANumber = new(new Vector2(float.NaN, 0f), Vector2.One);
        Bounds infinity = new(Vector2.Zero, new Vector2(float.PositiveInfinity, 1f));

        Assert.True(invertedX.IsEmpty);
        Assert.True(invertedY.IsEmpty);
        Assert.True(negativeSize.IsEmpty);
        Assert.True(notANumber.IsEmpty);
        Assert.True(infinity.IsEmpty);
        Assert.Equal(Vector2.Zero, invertedX.Center);
        Assert.Equal(Vector2.Zero, invertedX.Size);
        Assert.False(invertedX.Contains(new Vector2(1.5f, 0.5f)));
    }

    [Fact]
    public void CenterAndSizeConstructorDescribesTheBox()
    {
        Bounds box = new(new Vector2(1f, 2f), new Vector2(4f, 6f));

        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(1f, 2f), box.Center);
        Assert.Equal(new Vector2(4f, 6f), box.Size);
        Assert.Equal(new Vector2(2f, 3f), box.Extents);
        Assert.Equal(new Vector2(-1f, -1f), box.Min);
        Assert.Equal(new Vector2(3f, 5f), box.Max);
    }

    [Fact]
    public void FromMinMaxUsesTheTwoCorners()
    {
        Bounds box = Bounds.FromMinMax(new Vector2(1f, 2f), new Vector2(5f, 8f));

        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(3f, 5f), box.Center);
        Assert.Equal(new Vector2(4f, 6f), box.Size);
        Assert.Equal(new Vector2(2f, 3f), box.Extents);
        Assert.Equal(new Vector2(1f, 2f), box.Min);
        Assert.Equal(new Vector2(5f, 8f), box.Max);
    }

    [Fact]
    public void PropertySettersUpdateTheBox()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        box.Center = new Vector2(5f, 5f);
        Assert.Equal(Vector2.Zero, box.Min);
        Assert.Equal(new Vector2(10f, 10f), box.Max);

        box.Size = new Vector2(4f, 2f);
        Assert.Equal(new Vector2(2f, 1f), box.Extents);
        Assert.Equal(new Vector2(3f, 4f), box.Min);
        Assert.Equal(new Vector2(7f, 6f), box.Max);

        box.Extents = new Vector2(1f, 1f);
        Assert.Equal(new Vector2(2f, 2f), box.Size);
        Assert.Equal(new Vector2(4f, 4f), box.Min);
        Assert.Equal(new Vector2(6f, 6f), box.Max);

        box.Min = Vector2.Zero;
        Assert.Equal(new Vector2(6f, 6f), box.Max);
        Assert.Equal(new Vector2(3f, 3f), box.Center);

        box.Max = new Vector2(10f, 8f);
        Assert.Equal(Vector2.Zero, box.Min);
        Assert.Equal(new Vector2(5f, 4f), box.Center);
        Assert.Equal(new Vector2(10f, 8f), box.Size);
    }

    [Fact]
    public void EmptyGettersAreZeroWhileMinMaxKeepTheRawEdges()
    {
        Bounds point = Bounds.FromMinMax(new Vector2(5f, 7f), new Vector2(5f, 7f));

        Assert.True(point.IsEmpty);
        Assert.Equal(Vector2.Zero, point.Center);
        Assert.Equal(Vector2.Zero, point.Size);
        Assert.Equal(Vector2.Zero, point.Extents);
        Assert.Equal(new Vector2(5f, 7f), point.Min);
        Assert.Equal(new Vector2(5f, 7f), point.Max);
    }

    [Fact]
    public void SettersStoreRawValuesOnAnEmptyBox()
    {
        var box = default(Bounds);
        box.Center = new Vector2(3f, 4f);

        Assert.True(box.IsEmpty);
        Assert.Equal(new Vector2(3f, 4f), box.Min);
        Assert.Equal(new Vector2(3f, 4f), box.Max);
        Assert.Equal(Vector2.Zero, box.Center);

        box.Extents = new Vector2(2f, 3f);
        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(3f, 4f), box.Center);
        Assert.Equal(new Vector2(2f, 3f), box.Extents);
    }

    [Fact]
    public void ContainsIsInclusiveAtTheEdges()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 5f));

        Assert.True(box.Contains(new Vector2(5f, 2.5f)));
        Assert.True(box.Contains(Vector2.Zero));
        Assert.True(box.Contains(new Vector2(10f, 5f)));
        Assert.True(box.Contains(new Vector2(0f, 5f)));
        Assert.False(box.Contains(new Vector2(-0.001f, 0f)));
        Assert.False(box.Contains(new Vector2(10.001f, 5f)));
        Assert.False(box.Contains(new Vector2(float.NaN, 2f)));
        Assert.False(Bounds.Empty.Contains(Vector2.Zero));
    }

    [Fact]
    public void IntersectsIncludesTouchingEdgesAndCorners()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        Bounds overlapping = Bounds.FromMinMax(new Vector2(5f, 5f), new Vector2(15f, 15f));
        Bounds contained = Bounds.FromMinMax(new Vector2(2f, 2f), new Vector2(3f, 3f));
        Bounds touchingEdge = Bounds.FromMinMax(new Vector2(10f, 2f), new Vector2(12f, 4f));
        Bounds touchingCorner = Bounds.FromMinMax(new Vector2(10f, 10f), new Vector2(12f, 12f));
        Bounds separate = Bounds.FromMinMax(new Vector2(10.001f, 0f), new Vector2(12f, 2f));

        Assert.True(box.Intersects(box));
        Assert.True(box.Intersects(overlapping));
        Assert.True(overlapping.Intersects(box));
        Assert.True(box.Intersects(contained));
        Assert.True(contained.Intersects(box));
        Assert.True(box.Intersects(touchingEdge));
        Assert.True(box.Intersects(touchingCorner));
        Assert.False(box.Intersects(separate));
        Assert.False(separate.Intersects(box));
        Assert.False(box.Intersects(Bounds.Empty));
        Assert.False(Bounds.Empty.Intersects(box));
        Assert.False(Bounds.Empty.Intersects(Bounds.Empty));
    }

    [Fact]
    public void IntersectsIsSymmetricAndEmptySafeForInvalidBoxes()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        Bounds inverted = Bounds.FromMinMax(new Vector2(10f, 10f), Vector2.Zero);

        Assert.False(box.Intersects(inverted));
        Assert.False(inverted.Intersects(box));
    }

    [Fact]
    public void ClosestPointClampsToTheBox()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        Assert.Equal(new Vector2(5f, 5f), box.ClosestPoint(new Vector2(5f, 5f)));
        Assert.Equal(new Vector2(0f, 5f), box.ClosestPoint(new Vector2(-3f, 5f)));
        Assert.Equal(new Vector2(0f, 10f), box.ClosestPoint(new Vector2(-3f, 15f)));
        Assert.Equal(new Vector2(10f, 0f), box.ClosestPoint(new Vector2(15f, -3f)));
        Assert.Equal(new Vector2(0f, 5f), box.ClosestPoint(new Vector2(0f, 5f)));
        Assert.Equal(Vector2.Zero, Bounds.Empty.ClosestPoint(new Vector2(5f, 5f)));
    }

    [Fact]
    public void SqrDistanceIsZeroInsideAndPositiveOutside()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        Assert.Equal(0f, box.SqrDistance(new Vector2(5f, 5f)));
        Assert.Equal(0f, box.SqrDistance(new Vector2(0f, 10f)));
        Assert.Equal(25f, box.SqrDistance(new Vector2(-5f, 5f)));
        Assert.Equal(18f, box.SqrDistance(new Vector2(-3f, -3f)));
        Assert.Equal(50f, box.SqrDistance(new Vector2(-5f, 15f)));
        Assert.Equal(float.PositiveInfinity, Bounds.Empty.SqrDistance(Vector2.Zero));
    }

    [Fact]
    public void EncapsulatePointGrowsTheBox()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        box.Encapsulate(new Vector2(20f, -5f));

        Assert.Equal(new Vector2(0f, -5f), box.Min);
        Assert.Equal(new Vector2(20f, 10f), box.Max);
        Assert.Equal(new Vector2(10f, 2.5f), box.Center);
        Assert.Equal(new Vector2(20f, 15f), box.Size);

        box.Encapsulate(new Vector2(5f, 5f));
        Assert.Equal(new Vector2(0f, -5f), box.Min);
        Assert.Equal(new Vector2(20f, 10f), box.Max);
    }

    [Fact]
    public void EncapsulateFromEmptyGrowsFromTheOrigin()
    {
        Bounds positive = Bounds.Empty;
        positive.Encapsulate(new Vector2(2f, 3f));

        Assert.False(positive.IsEmpty);
        Assert.Equal(Vector2.Zero, positive.Min);
        Assert.Equal(new Vector2(2f, 3f), positive.Max);
        Assert.Equal(new Vector2(2f, 3f), positive.Size);

        Bounds negative = Bounds.Empty;
        negative.Encapsulate(new Vector2(-4f, -2f));

        Assert.False(negative.IsEmpty);
        Assert.Equal(new Vector2(-4f, -2f), negative.Min);
        Assert.Equal(Vector2.Zero, negative.Max);
    }

    [Fact]
    public void EncapsulateFromAZeroSizeBoxGrowsFromThatPoint()
    {
        Bounds box = Bounds.FromMinMax(new Vector2(1f, 2f), new Vector2(1f, 2f));
        box.Encapsulate(new Vector2(4f, 6f));

        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(1f, 2f), box.Min);
        Assert.Equal(new Vector2(4f, 6f), box.Max);
    }

    [Fact]
    public void EncapsulateBoundsIncludesBothBoxes()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(2f, 2f));
        box.Encapsulate(Bounds.FromMinMax(new Vector2(5f, -1f), new Vector2(6f, 1f)));

        Assert.Equal(new Vector2(0f, -1f), box.Min);
        Assert.Equal(new Vector2(6f, 2f), box.Max);
        Assert.Equal(new Vector2(3f, 0.5f), box.Center);

        Bounds fromEmpty = Bounds.Empty;
        fromEmpty.Encapsulate(Bounds.FromMinMax(new Vector2(2f, 2f), new Vector2(4f, 4f)));

        Assert.Equal(Vector2.Zero, fromEmpty.Min);
        Assert.Equal(new Vector2(4f, 4f), fromEmpty.Max);
    }

    [Fact]
    public void ExpandFloatGrowsEverySideByHalfTheAmount()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        box.Expand(2f);

        Assert.Equal(new Vector2(-1f, -1f), box.Min);
        Assert.Equal(new Vector2(11f, 11f), box.Max);
        Assert.Equal(new Vector2(12f, 12f), box.Size);

        box.Expand(0f);
        Assert.Equal(new Vector2(12f, 12f), box.Size);

        box.Expand(-2f);
        Assert.Equal(new Vector2(10f, 10f), box.Size);

        box.Expand(-100f);
        Assert.True(box.IsEmpty);
    }

    [Fact]
    public void ExpandVectorGrowsEachAxisIndependently()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        box.Expand(new Vector2(2f, 4f));

        Assert.Equal(new Vector2(-1f, -2f), box.Min);
        Assert.Equal(new Vector2(11f, 12f), box.Max);
        Assert.Equal(new Vector2(12f, 14f), box.Size);
    }

    [Fact]
    public void ExpandTurnsAnEmptyBoxIntoANonEmptyOne()
    {
        Bounds uniform = Bounds.Empty;
        uniform.Expand(4f);

        Assert.False(uniform.IsEmpty);
        Assert.Equal(new Vector2(-2f, -2f), uniform.Min);
        Assert.Equal(new Vector2(2f, 2f), uniform.Max);

        Bounds perAxis = Bounds.Empty;
        perAxis.Expand(new Vector2(2f, 6f));

        Assert.False(perAxis.IsEmpty);
        Assert.Equal(new Vector2(-1f, -3f), perAxis.Min);
        Assert.Equal(new Vector2(1f, 3f), perAxis.Max);
    }

    [Fact]
    public void SetMinMaxReplacesTheBox()
    {
        Bounds box = Bounds.Empty;
        box.SetMinMax(new Vector2(1f, 2f), new Vector2(5f, 8f));

        Assert.False(box.IsEmpty);
        Assert.Equal(new Vector2(3f, 5f), box.Center);
        Assert.Equal(new Vector2(4f, 6f), box.Size);

        box.SetMinMax(new Vector2(5f, 8f), new Vector2(1f, 2f));

        Assert.True(box.IsEmpty);
        Assert.Equal(new Vector2(5f, 8f), box.Min);
        Assert.Equal(new Vector2(1f, 2f), box.Max);
    }

    [Fact]
    public void ToStringUsesTheF2DefaultAndInvariantCulture()
    {
        Bounds box = new(new Vector2(1f, 2f), new Vector2(4f, 6f));

        Assert.Equal("Center: (1.00, 2.00), Extents: (2.00, 3.00)", box.ToString());
        Assert.Equal("Center: (1.00, 2.00), Extents: (2.00, 3.00)", box.ToString(string.Empty));
        Assert.Equal("Center: (1.0, 2.0), Extents: (2.0, 3.0)", box.ToString("F1"));
        Assert.Equal(
            "Center: (1, 2), Extents: (2, 3)",
            box.ToString("F0", CultureInfo.InvariantCulture));
        Assert.Equal("Center: (0.00, 0.00), Extents: (0.00, 0.00)", Bounds.Empty.ToString());
    }

    [Fact]
    public void ToStringUsesTheGivenFormatProvider()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        Bounds box = new(new Vector2(1.5f, 2.5f), new Vector2(4f, 6f));

        Assert.Equal("Center: (1,50, 2,50), Extents: (2,00, 3,00)", box.ToString("F2", culture));
        Assert.Equal(
            "Center: (1.50, 2.50), Extents: (2.00, 3.00)",
            ((IFormattable)box).ToString("F2", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DeconstructReturnsTheRawCorners()
    {
        Bounds box = Bounds.FromMinMax(new Vector2(1f, 2f), new Vector2(5f, 8f));
        (Vector2 min, Vector2 max) = box;

        Assert.Equal(new Vector2(1f, 2f), min);
        Assert.Equal(new Vector2(5f, 8f), max);

        Bounds point = Bounds.FromMinMax(new Vector2(3f, 4f), new Vector2(3f, 4f));
        (Vector2 rawMin, Vector2 rawMax) = point;

        Assert.Equal(new Vector2(3f, 4f), rawMin);
        Assert.Equal(new Vector2(3f, 4f), rawMax);
    }

    [Fact]
    public void EqualityUsesTheRawCenterAndExtents()
    {
        Bounds box = new(new Vector2(1f, 2f), new Vector2(4f, 6f));
        Bounds same = Bounds.FromMinMax(new Vector2(-1f, -1f), new Vector2(3f, 5f));
        Bounds differentCenter = new(new Vector2(2f, 2f), new Vector2(4f, 6f));
        Bounds differentSize = new(new Vector2(1f, 2f), new Vector2(4f, 7f));

        Assert.True(box == same);
        Assert.False(box != same);
        Assert.True(box.Equals(same));
        Assert.True(box.Equals((object)same));
        Assert.Equal(box.GetHashCode(), same.GetHashCode());
        Assert.False(box == differentCenter);
        Assert.True(box != differentCenter);
        Assert.False(box == differentSize);
        Assert.True(box != differentSize);
        Assert.False(box.Equals("bounds"));
        Assert.False(box.Equals((object?)null));
    }

    [Fact]
    public void EmptyBoxesCompareByTheirRawValues()
    {
        Bounds degenerate = Bounds.FromMinMax(new Vector2(5f, 5f), new Vector2(5f, 5f));
        Bounds sameDegenerate = Bounds.FromMinMax(new Vector2(5f, 5f), new Vector2(5f, 5f));

        Assert.True(degenerate.IsEmpty);
        Assert.NotEqual(Bounds.Empty, degenerate);
        Assert.Equal(degenerate, sameDegenerate);
        Assert.Equal(Bounds.Empty, default(Bounds));
        Assert.True(Bounds.Empty == default);
        Assert.False(Bounds.Empty != default);
    }

    [Fact]
    public void GetHashCodeMatchesUnityFormulaForNonEmptyBoxes()
    {
        Bounds box = new(new Vector2(1f, 2f), new Vector2(4f, 6f));

        Assert.Equal(
            box.Center.GetHashCode() ^ (box.Extents.GetHashCode() << 2),
            box.GetHashCode());
    }

    [Fact]
    public void IntersectRayHitsAndReportsDistance()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(-5f, 5f), Vector2.UnitX), out float distance));
        Assert.Equal(5f, distance);

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(20f, 5f), -Vector2.UnitX), out distance));
        Assert.Equal(10f, distance);

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(5f, -4f), Vector2.UnitY), out distance));
        Assert.Equal(4f, distance);

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(-5f, 0f), Vector2.UnitX), out distance));
        Assert.Equal(5f, distance);
    }

    [Fact]
    public void IntersectRayReturnsZeroWhenTheOriginIsInside()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(5f, 5f), Vector2.UnitX), out float distance));
        Assert.Equal(0f, distance);

        Assert.True(
            box.IntersectRay(new Ray2D(Vector2.Zero, Vector2.One), out distance));
        Assert.Equal(0f, distance);

        Assert.True(
            box.IntersectRay(new Ray2D(new Vector2(10f, 10f), Vector2.UnitX), out distance));
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void IntersectRayMissesWhenOutsideParallelOrPointingAway()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));

        Assert.False(
            box.IntersectRay(
                new Ray2D(new Vector2(-5f, 20f), Vector2.UnitX),
                out float distance));
        Assert.Equal(0f, distance);

        Assert.False(
            box.IntersectRay(new Ray2D(new Vector2(-5f, 5f), Vector2.UnitY)));
        Assert.False(
            box.IntersectRay(new Ray2D(new Vector2(20f, 5f), Vector2.UnitX)));
    }

    [Fact]
    public void IntersectRayHandlesZeroDirectionRays()
    {
        Bounds box = Bounds.FromMinMax(Vector2.Zero, new Vector2(10f, 10f));
        var inside = new Ray2D(new Vector2(5f, 5f), Vector2.Zero);
        var outside = new Ray2D(new Vector2(-1f, 5f), Vector2.Zero);

        Assert.Equal(Vector2.Zero, inside.Direction);
        Assert.True(box.IntersectRay(inside, out float distance));
        Assert.Equal(0f, distance);
        Assert.False(box.IntersectRay(outside, out distance));
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void IntersectRayReturnsFalseForAnEmptyBox()
    {
        var ray = new Ray2D(Vector2.Zero, Vector2.UnitX);

        Assert.False(Bounds.Empty.IntersectRay(ray, out float distance));
        Assert.Equal(0f, distance);
        Assert.False(Bounds.Empty.IntersectRay(ray));
    }
}
