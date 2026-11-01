using System.Numerics;
using Bork2d.Engine.Mathematics;
using Xunit;

namespace Bork2d.Tests.Mathematics;

/// <summary>
/// Deterministic tests for the Unity 6000.6 <see cref="Vector2"/> extension surface: length aliases,
/// direction constants, angles, clamping, movement, interpolation, smoothing, normalization, and
/// approximate equality, plus coexistence with the existing <see cref="Vector2"/> members.
/// </summary>
public sealed class Vector2ExtensionsTests
{
    private const int AnglePrecision = 3;

    [Fact]
    public void MagnitudeReturnsTheVectorLength()
    {
        Assert.Equal(5f, new Vector2(3f, 4f).Magnitude);
        Assert.Equal(5f, new Vector2(-3f, -4f).Magnitude);
        Assert.Equal(0f, Vector2.Zero.Magnitude);
        Assert.Equal(13f, new Vector2(5f, -12f).Magnitude);
    }

    [Fact]
    public void SqrMagnitudeReturnsTheSquaredLength()
    {
        Assert.Equal(25f, new Vector2(3f, 4f).SqrMagnitude);
        Assert.Equal(25f, new Vector2(-3f, 4f).SqrMagnitude);
        Assert.Equal(0f, Vector2.Zero.SqrMagnitude);
    }

    [Fact]
    public void NormalizedReturnsAUnitVector()
    {
        Vector2 normalized = new Vector2(3f, 4f).Normalized;

        Assert.Equal(new Vector2(0.6f, 0.8f), normalized);
        Assert.Equal(1f, normalized.Magnitude, 6);
        Assert.Equal(new Vector2(-0.6f, -0.8f), new Vector2(-3f, -4f).Normalized);
    }

    [Fact]
    public void NormalizedReturnsZeroForZeroAndTinyVectors()
    {
        Assert.Equal(Vector2.Zero, Vector2.Zero.Normalized);
        Assert.Equal(Vector2.Zero, new Vector2(1e-6f, -2e-6f).Normalized);
        Assert.Equal(new Vector2(1f, 0f), new Vector2(1e-3f, 0f).Normalized);
    }

    [Fact]
    public void DirectionConstantsMatchUnity()
    {
        Assert.Equal(new Vector2(0f, 1f), Vector2.Up);
        Assert.Equal(new Vector2(0f, -1f), Vector2.Down);
        Assert.Equal(new Vector2(-1f, 0f), Vector2.Left);
        Assert.Equal(new Vector2(1f, 0f), Vector2.Right);
    }

    [Fact]
    public void AngleMatchesUnityForAxisAlignedVectors()
    {
        Assert.Equal(0f, Vector2.Angle(Vector2.Right, Vector2.Right), AnglePrecision);
        Assert.Equal(90f, Vector2.Angle(Vector2.Right, Vector2.Up), AnglePrecision);
        Assert.Equal(90f, Vector2.Angle(Vector2.Right, Vector2.Down), AnglePrecision);
        Assert.Equal(90f, Vector2.Angle(Vector2.Up, Vector2.Right), AnglePrecision);
        Assert.Equal(180f, Vector2.Angle(Vector2.Right, Vector2.Left), AnglePrecision);
        Assert.Equal(45f, Vector2.Angle(Vector2.Right, new Vector2(1f, 1f)), AnglePrecision);
    }

    [Fact]
    public void AngleIgnoresMagnitudeAndReturnsZeroForZeroVectors()
    {
        Assert.Equal(
            90f,
            Vector2.Angle(new Vector2(10f, 0f), new Vector2(0f, 0.5f)),
            AnglePrecision);
        Assert.Equal(0f, Vector2.Angle(Vector2.Zero, Vector2.Right));
        Assert.Equal(0f, Vector2.Angle(Vector2.Zero, Vector2.Zero));
    }

    [Fact]
    public void SignedAngleIsPositiveCounterClockwise()
    {
        Assert.Equal(90f, Vector2.SignedAngle(Vector2.Right, Vector2.Up), AnglePrecision);
        Assert.Equal(-90f, Vector2.SignedAngle(Vector2.Right, Vector2.Down), AnglePrecision);
        Assert.Equal(-90f, Vector2.SignedAngle(Vector2.Up, Vector2.Right), AnglePrecision);
        Assert.Equal(45f, Vector2.SignedAngle(Vector2.Right, new Vector2(1f, 1f)), AnglePrecision);
        Assert.Equal(-45f, Vector2.SignedAngle(Vector2.Right, new Vector2(1f, -1f)), AnglePrecision);
    }

    [Fact]
    public void SignedAngleUsesPositiveSignWhenTheCrossProductIsZero()
    {
        Assert.Equal(180f, Vector2.SignedAngle(Vector2.Right, Vector2.Left), AnglePrecision);
        Assert.Equal(0f, Vector2.SignedAngle(Vector2.Right, Vector2.Right));

        float zeroVectorAngle = Vector2.SignedAngle(Vector2.Zero, Vector2.Down);
        Assert.Equal(0f, zeroVectorAngle);
        Assert.False(float.IsNegative(zeroVectorAngle));
    }

    [Fact]
    public void ClampMagnitudeLimitsOnlyVectorsThatAreTooLong()
    {
        Assert.Equal(new Vector2(5f, 0f), Vector2.ClampMagnitude(new Vector2(10f, 0f), 5f));
        Assert.Equal(
            new Vector2(0.6f, 0.8f) * 5f,
            Vector2.ClampMagnitude(new Vector2(3f, 4f), 5f));
        Assert.Equal(new Vector2(3f, 4f), Vector2.ClampMagnitude(new Vector2(3f, 4f), 10f));
        Assert.Equal(new Vector2(0f, -2f), Vector2.ClampMagnitude(new Vector2(0f, -20f), 2f));
    }

    [Fact]
    public void ClampMagnitudeReturnsZeroForZeroMaxLength()
    {
        Assert.Equal(Vector2.Zero, Vector2.ClampMagnitude(new Vector2(3f, 4f), 0f));
        Assert.Equal(Vector2.Zero, Vector2.ClampMagnitude(Vector2.Zero, 0f));
    }

    [Fact]
    public void MoveTowardsReachesTheTargetWithinTheDelta()
    {
        Assert.Equal(
            new Vector2(1f, 0f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(1f, 0f), 2f));
        Assert.Equal(
            new Vector2(1f, 0f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(1f, 0f), 1f));
        Assert.Equal(
            new Vector2(1f, 0f),
            Vector2.MoveTowards(new Vector2(1f, 0f), new Vector2(1f, 0f), 5f));
    }

    [Fact]
    public void MoveTowardsStopsAtTheMaximumDistance()
    {
        Assert.Equal(
            new Vector2(3f, 0f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(10f, 0f), 3f));
        Assert.Equal(
            new Vector2(0f, 2f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(0f, 10f), 2f));
        Assert.Equal(
            new Vector2(3f, 4f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(6f, 8f), 5f));
    }

    [Fact]
    public void MoveTowardsMovesAwayFromTheTargetForANegativeDelta()
    {
        Assert.Equal(
            new Vector2(-1f, 0f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(1f, 0f), -1f));
        Assert.Equal(
            new Vector2(-0.5f, 0f),
            Vector2.MoveTowards(Vector2.Zero, new Vector2(1f, 0f), -0.5f));

        Vector2 diagonal = Vector2.MoveTowards(Vector2.Zero, new Vector2(3f, 4f), -5f);
        Assert.Equal(-3f, diagonal.X, 6);
        Assert.Equal(-4f, diagonal.Y, 6);
    }

    [Fact]
    public void PerpendicularRotatesNinetyDegreesCounterClockwise()
    {
        Assert.Equal(new Vector2(-3f, 2f), Vector2.Perpendicular(new Vector2(2f, 3f)));
        Assert.Equal(new Vector2(-1f, 0f), Vector2.Perpendicular(Vector2.Up));
        Assert.Equal(Vector2.Zero, Vector2.Perpendicular(Vector2.Zero));
    }

    [Fact]
    public void StaticScaleMultipliesComponents()
    {
        Assert.Equal(
            new Vector2(8f, 15f),
            Vector2.Scale(new Vector2(2f, 3f), new Vector2(4f, 5f)));
        Assert.Equal(
            new Vector2(-3f, -8f),
            Vector2.Scale(new Vector2(-1f, 2f), new Vector2(3f, -4f)));
    }

    [Fact]
    public void InstanceScaleMultipliesComponentsInPlace()
    {
        Vector2 value = new(2f, 3f);

        value.Scale(new Vector2(4f, 5f));

        Assert.Equal(new Vector2(8f, 15f), value);
    }

    [Fact]
    public void LerpUnclampedDoesNotClampTheRatio()
    {
        Vector2 a = new(0f, 0f);
        Vector2 b = new(10f, 20f);

        Assert.Equal(new Vector2(5f, 10f), Vector2.LerpUnclamped(a, b, 0.5f));
        Assert.Equal(new Vector2(20f, 40f), Vector2.LerpUnclamped(a, b, 2f));
        Assert.Equal(new Vector2(-10f, -20f), Vector2.LerpUnclamped(a, b, -1f));
    }

    [Fact]
    public void LerpClampedClampsTheRatio()
    {
        Vector2 a = new(0f, 0f);
        Vector2 b = new(10f, 20f);

        Assert.Equal(new Vector2(5f, 10f), Vector2.LerpClamped(a, b, 0.5f));
        Assert.Equal(new Vector2(10f, 20f), Vector2.LerpClamped(a, b, 2f));
        Assert.Equal(new Vector2(0f, 0f), Vector2.LerpClamped(a, b, -1f));
        Assert.Equal(new Vector2(0f, 0f), Vector2.LerpClamped(a, b, 0f));
        Assert.Equal(new Vector2(10f, 20f), Vector2.LerpClamped(a, b, 1f));
    }

    [Fact]
    public void NormalizeOrZeroNormalizesOrReturnsZero()
    {
        Assert.Equal(new Vector2(0.6f, 0.8f), Vector2.NormalizeOrZero(new Vector2(3f, 4f)));
        Assert.Equal(new Vector2(0f, -1f), Vector2.NormalizeOrZero(new Vector2(0f, -5f)));
        Assert.Equal(Vector2.Zero, Vector2.NormalizeOrZero(Vector2.Zero));
        Assert.Equal(Vector2.Zero, Vector2.NormalizeOrZero(new Vector2(1e-6f, 1e-6f)));
    }

    [Fact]
    public void StaticApproximatelyEqualsUsesUnityEpsilon()
    {
        Assert.True(Vector2.ApproximatelyEquals(new Vector2(1f, 2f), new Vector2(1f, 2f)));
        Assert.True(
            Vector2.ApproximatelyEquals(new Vector2(1f, 2f), new Vector2(1f + 1e-6f, 2f)));
        Assert.False(
            Vector2.ApproximatelyEquals(new Vector2(1f, 2f), new Vector2(1f + 1e-4f, 2f)));
        Assert.False(
            Vector2.ApproximatelyEquals(Vector2.Zero, new Vector2(1e-3f, 0f)));
        Assert.True(Vector2.ApproximatelyEquals(Vector2.Zero, Vector2.Zero));
    }

    [Fact]
    public void InstanceApproximatelyEqualsUsesUnityEpsilon()
    {
        Vector2 value = new(1f, 2f);

        Assert.True(value.ApproximatelyEquals(new Vector2(1f, 2f)));
        Assert.True(value.ApproximatelyEquals(new Vector2(1f + 1e-6f, 2f - 1e-6f)));
        Assert.False(value.ApproximatelyEquals(new Vector2(1f + 1e-4f, 2f)));
        Assert.False(Vector2.Zero.ApproximatelyEquals(new Vector2(0f, 1e-3f)));
        Assert.True(Vector2.Zero.ApproximatelyEquals(Vector2.Zero));
    }

    [Fact]
    public void SmoothDampProducesTheUnityPositionAndVelocity()
    {
        Vector2 velocity = Vector2.Zero;

        Vector2 position = Vector2.SmoothDamp(
            Vector2.Zero,
            new Vector2(1f, 0f),
            ref velocity,
            0.3f,
            1000f,
            0.02f);

        Assert.Equal(0.007957160472869873f, position.X, 6);
        Assert.Equal(0f, position.Y);
        Assert.Equal(0.7780727744102478f, velocity.X, 6);
        Assert.Equal(0f, velocity.Y);
    }

    [Fact]
    public void SmoothDampClampsTheDistanceToMaxSpeed()
    {
        Vector2 velocity = Vector2.Zero;

        Vector2 position = Vector2.SmoothDamp(
            Vector2.Zero,
            new Vector2(10f, 0f),
            ref velocity,
            0.3f,
            5f,
            0.02f);

        Assert.Equal(0.011935710906982422f, position.X, 6);
        Assert.Equal(1.1671091318130493f, velocity.X, 6);
        Assert.Equal(0f, position.Y);
        Assert.Equal(0f, velocity.Y);
    }

    [Fact]
    public void SmoothDampUsesTheMinimumSmoothTime()
    {
        Vector2 zeroVelocity = Vector2.Zero;
        Vector2 minimumVelocity = Vector2.Zero;

        Vector2 fromZero = Vector2.SmoothDamp(
            Vector2.Zero,
            Vector2.Right,
            ref zeroVelocity,
            0f,
            1000f,
            0.02f);
        Vector2 fromMinimum = Vector2.SmoothDamp(
            Vector2.Zero,
            Vector2.Right,
            ref minimumVelocity,
            0.0001f,
            1000f,
            0.02f);

        Assert.Equal(fromMinimum, fromZero);
        Assert.Equal(minimumVelocity, zeroVelocity);
    }

    [Fact]
    public void SmoothDampConvergesToTheTargetWithoutOvershoot()
    {
        Vector2 target = new(2f, 3f);
        Vector2 position = Vector2.Zero;
        Vector2 velocity = Vector2.Zero;

        for (int step = 0; step < 240; step++)
        {
            position = Vector2.SmoothDamp(position, target, ref velocity, 0.2f, 1000f, 1f / 60f);

            Assert.True(position.X <= target.X, $"overshoot on X at step {step}");
            Assert.True(position.Y <= target.Y, $"overshoot on Y at step {step}");
        }

        Assert.Equal(0f, Vector2.Distance(position, target), 5);
        Assert.Equal(0f, velocity.X, 5);
        Assert.Equal(0f, velocity.Y, 5);
    }

    [Fact]
    public void SetReplacesBothComponents()
    {
        Vector2 value = new(1f, 2f);

        value.Set(3f, 4f);
        Assert.Equal(new Vector2(3f, 4f), value);

        value.Set(-1.5f, 0f);
        Assert.Equal(new Vector2(-1.5f, 0f), value);
    }

    [Fact]
    public void NormalizeMutatesTheVectorInPlace()
    {
        Vector2 value = new(3f, 4f);

        value.Normalize();

        Assert.Equal(new Vector2(0.6f, 0.8f), value);
        Assert.Equal(1f, value.Magnitude, 6);
    }

    [Fact]
    public void NormalizeMutatesZeroAndTinyVectorsToZero()
    {
        Vector2 zero = Vector2.Zero;
        Vector2 tiny = new(1e-6f, -2e-6f);

        zero.Normalize();
        tiny.Normalize();

        Assert.Equal(Vector2.Zero, zero);
        Assert.Equal(Vector2.Zero, tiny);
    }

    [Fact]
    public void ExtensionMembersResolveTogetherWithTheExistingVector2Surface()
    {
        Vector2 value = new(3f, 4f);

        // Extension members from Bork2d.Engine.Mathematics.
        Assert.Equal(5f, value.Magnitude);
        Assert.Equal(25f, value.SqrMagnitude);
        Assert.Equal(new Vector2(0f, 1f), Vector2.Up);

        // Existing System.Numerics.Vector2 members still resolve and keep .NET semantics.
        Assert.Equal(5f, value.Length());
        Assert.Equal(25f, value.LengthSquared());
        Assert.Equal(5f, Vector2.Distance(Vector2.Zero, value));
        Assert.Equal(25f, Vector2.Dot(value, value));
        Assert.Equal(new Vector2(20f, 0f), Vector2.Lerp(Vector2.Zero, new Vector2(10f, 0f), 2f));
        Assert.True(float.IsNaN(Vector2.Normalize(Vector2.Zero).X));
        Assert.True(value == new Vector2(3f, 4f));
        Assert.False(value == new Vector2(3f, 4.000001f));
    }
}
