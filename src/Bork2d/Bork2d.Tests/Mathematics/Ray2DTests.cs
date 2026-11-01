using System.Globalization;
using System.Numerics;
using Bork2d.Engine.Mathematics;
using Xunit;

namespace Bork2d.Tests.Mathematics;

/// <summary>
/// Deterministic tests for <see cref="Ray2D"/>: origin/direction construction and setters,
/// normalization, point evaluation, the default value, and formatting.
/// </summary>
public sealed class Ray2DTests
{
    [Fact]
    public void ConstructorNormalizesTheDirection()
    {
        var axis = new Ray2D(new Vector2(1f, 2f), new Vector2(0f, 5f));

        Assert.Equal(new Vector2(1f, 2f), axis.Origin);
        Assert.Equal(Vector2.UnitY, axis.Direction);

        var diagonal = new Ray2D(Vector2.Zero, new Vector2(3f, 4f));

        Assert.Equal(new Vector2(0.6f, 0.8f), diagonal.Direction);
    }

    [Fact]
    public void ConstructorLeavesZeroAndTinyDirectionsAtZero()
    {
        var zero = new Ray2D(new Vector2(2f, 3f), Vector2.Zero);
        var tiny = new Ray2D(new Vector2(2f, 3f), new Vector2(1e-6f, -1e-6f));

        Assert.Equal(Vector2.Zero, zero.Direction);
        Assert.Equal(new Vector2(2f, 3f), zero.Origin);
        Assert.Equal(Vector2.Zero, tiny.Direction);
    }

    [Fact]
    public void DefaultValueIsAZeroRayAtTheOrigin()
    {
        Ray2D ray = default;

        Assert.Equal(Vector2.Zero, ray.Origin);
        Assert.Equal(Vector2.Zero, ray.Direction);
        Assert.Equal(Vector2.Zero, ray.GetPoint(0f));
        Assert.Equal(Vector2.Zero, ray.GetPoint(5f));
        Assert.Equal(Vector2.Zero, ray.GetPoint(-5f));
    }

    [Fact]
    public void OriginSetterStoresTheRawValue()
    {
        var ray = new Ray2D(Vector2.Zero, Vector2.UnitX);
        ray.Origin = new Vector2(3f, 4f);

        Assert.Equal(new Vector2(3f, 4f), ray.Origin);
        Assert.Equal(Vector2.UnitX, ray.Direction);
    }

    [Fact]
    public void DirectionSetterRenormalizes()
    {
        var ray = new Ray2D(Vector2.Zero, Vector2.UnitX);

        ray.Direction = new Vector2(0f, 10f);
        Assert.Equal(Vector2.UnitY, ray.Direction);

        ray.Direction = new Vector2(3f, 4f);
        Assert.Equal(new Vector2(0.6f, 0.8f), ray.Direction);

        ray.Direction = Vector2.Zero;
        Assert.Equal(Vector2.Zero, ray.Direction);

        ray.Direction = new Vector2(1e-6f, 0f);
        Assert.Equal(Vector2.Zero, ray.Direction);
    }

    [Fact]
    public void GetPointWalksAlongTheDirection()
    {
        var ray = new Ray2D(new Vector2(1f, 2f), new Vector2(0f, 2f));

        Assert.Equal(new Vector2(1f, 2f), ray.GetPoint(0f));
        Assert.Equal(new Vector2(1f, 4.5f), ray.GetPoint(2.5f));
        Assert.Equal(new Vector2(1f, 0f), ray.GetPoint(-2f));
    }

    [Fact]
    public void ToStringUsesTheF2DefaultAndInvariantCulture()
    {
        var ray = new Ray2D(new Vector2(1f, 2f), new Vector2(3f, 4f));

        Assert.Equal("Origin: (1.00, 2.00), Dir: (0.60, 0.80)", ray.ToString());
        Assert.Equal("Origin: (1.00, 2.00), Dir: (0.60, 0.80)", ray.ToString(string.Empty));
        Assert.Equal("Origin: (1.0, 2.0), Dir: (0.6, 0.8)", ray.ToString("F1"));
        Assert.Equal(
            "Origin: (1, 2), Dir: (1, 1)",
            ray.ToString("F0", CultureInfo.InvariantCulture));
        Assert.Equal("Origin: (0.00, 0.00), Dir: (0.00, 0.00)", default(Ray2D).ToString());
    }

    [Fact]
    public void ToStringUsesTheGivenFormatProvider()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        var ray = new Ray2D(new Vector2(1.5f, 2.5f), Vector2.UnitY);

        Assert.Equal("Origin: (1,50, 2,50), Dir: (0,00, 1,00)", ray.ToString("F2", culture));
        Assert.Equal(
            "Origin: (1.50, 2.50), Dir: (0.00, 1.00)",
            ((IFormattable)ray).ToString("F2", CultureInfo.InvariantCulture));
    }
}
