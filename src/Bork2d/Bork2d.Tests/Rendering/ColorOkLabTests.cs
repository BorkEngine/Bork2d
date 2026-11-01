using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the OkLab support on <see cref="Color"/>: conversion of linear sRGB
/// primaries and grays, the absence of gamma conversion, round trips, the unclamped inverse, alpha
/// handling, and the clamped and unclamped OkLab interpolation variants.
/// </summary>
public sealed class ColorOkLabTests
{
    private const float Tolerance = 1e-5f;

    [Theory]
    [InlineData(1f, 0f, 0f, 0.6279554f, 0.2248631f, 0.1258463f)]
    [InlineData(0f, 1f, 0f, 0.8664396f, -0.2338876f, 0.1794985f)]
    [InlineData(0f, 0f, 1f, 0.4520137f, -0.0324570f, -0.3115281f)]
    [InlineData(1f, 1f, 1f, 1f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    public void ToOkLabMatchesKnownLinearSrgbValues(
        float r,
        float g,
        float b,
        float expectedL,
        float expectedOpponentA,
        float expectedOpponentB)
    {
        new Color(r, g, b, 1f).ToOkLab(out float l, out float a, out float opponentB);

        AssertClose(expectedL, l);
        AssertClose(expectedOpponentA, a);
        AssertClose(expectedOpponentB, opponentB);
    }

    [Fact]
    public void ToOkLabTreatsComponentsAsLinearGrayWithoutGammaDecoding()
    {
        new Color(0.5f, 0.5f, 0.5f, 1f).ToOkLab(out float l, out float a, out float b);

        AssertClose(0.7937005f, l);
        AssertClose(0f, a);
        AssertClose(0f, b);
    }

    [Fact]
    public void ToOkLabIgnoresAlpha()
    {
        new Color(0.2f, 0.6f, 0.4f, 1f).ToOkLab(
            out float opaqueL,
            out float opaqueA,
            out float opaqueB);
        new Color(0.2f, 0.6f, 0.4f, 0.25f).ToOkLab(
            out float transparentL,
            out float transparentA,
            out float transparentB);

        AssertClose(opaqueL, transparentL);
        AssertClose(opaqueA, transparentA);
        AssertClose(opaqueB, transparentB);
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1f, 1f, 1f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.2f, 0.6f, 0.4f)]
    public void ToOkLabRoundTripsThroughFromOkLab(float r, float g, float b)
    {
        Color original = new(r, g, b, 1f);
        original.ToOkLab(out float l, out float a, out float opponentB);

        Color roundTripped = Color.FromOkLab(l, a, opponentB);

        AssertColorClose(original, roundTripped, 1e-4f);
    }

    [Fact]
    public void FromOkLabDefaultsAlphaToOneAndUsesExplicitAlpha()
    {
        new Color(0.2f, 0.6f, 0.4f, 0.35f).ToOkLab(out float l, out float a, out float b);

        Color defaultAlpha = Color.FromOkLab(l, a, b);
        Assert.Equal(1f, defaultAlpha.A);
        AssertColorClose(new Color(0.2f, 0.6f, 0.4f, 1f), defaultAlpha, 1e-4f);

        Color explicitAlpha = Color.FromOkLab(l, a, b, 0.35f);
        Assert.Equal(0.35f, explicitAlpha.A);
        AssertColorClose(new Color(0.2f, 0.6f, 0.4f, 0.35f), explicitAlpha, 1e-4f);
    }

    [Fact]
    public void FromOkLabDoesNotClampToTheLinearSrgbGamut()
    {
        Color color = Color.FromOkLab(0.5f, 0.5f, 0f);

        AssertClose(1.1133090f, color.R);
        AssertClose(-0.2304418f, color.G);
        AssertClose(0.0967787f, color.B);
        Assert.True(color.R > 1f);
        Assert.True(color.G < 0f);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    public void LerpOkLabReturnsItsEndpointColors()
    {
        Color a = new(0.2f, 0.6f, 0.4f, 0.3f);
        Color b = new(0.9f, 0.1f, 0.7f, 0.8f);

        AssertColorClose(a, Color.LerpOkLab(a, b, 0f), 1e-4f);
        AssertColorClose(b, Color.LerpOkLab(a, b, 1f), 1e-4f);
    }

    [Fact]
    public void LerpOkLabInterpolatesBlackToWhiteAtTheOkLabMidpoint()
    {
        Color midpoint = Color.LerpOkLab(Color.Black, Color.White, 0.5f);

        midpoint.ToOkLab(out float l, out float a, out float b);
        AssertClose(0.5f, l);
        AssertClose(0f, a);
        AssertClose(0f, b);
        AssertClose(0.125f, midpoint.R, 1e-4f);
        AssertClose(0.125f, midpoint.G, 1e-4f);
        AssertClose(0.125f, midpoint.B, 1e-4f);
        Assert.Equal(1f, midpoint.A);
    }

    [Fact]
    public void LerpOkLabDoesNotClampOutOfGamutResults()
    {
        Color midpoint = Color.LerpOkLab(Color.Green, Color.Blue, 0.5f);

        Assert.True(midpoint.R < 0f);
        AssertClose(-0.0819892f, midpoint.R, 1e-4f);
        AssertClose(0.4004264f, midpoint.G, 1e-4f);
        AssertClose(0.5193839f, midpoint.B, 1e-4f);
    }

    [Fact]
    public void LerpOkLabInterpolatesBlueToPureYellowInLinearSrgb()
    {
        // Pure yellow rather than the Color.Yellow preset, so the expected linear midpoint matches
        // the reference matrix output.
        Color blue = new(0f, 0f, 1f, 1f);
        Color yellow = new(1f, 1f, 0f, 1f);

        Color midpoint = Color.LerpOkLab(blue, yellow, 0.5f);

        AssertColorClose(new Color(0.1492253f, 0.4096393f, 0.5713724f, 1f), midpoint, 1e-4f);
    }

    [Fact]
    public void LerpOkLabClampsT()
    {
        Color a = new(0.2f, 0.6f, 0.4f, 0.3f);
        Color b = new(0.9f, 0.1f, 0.7f, 0.8f);

        AssertColorClose(a, Color.LerpOkLab(a, b, -0.5f), 1e-4f);
        AssertColorClose(b, Color.LerpOkLab(a, b, 1.5f), 1e-4f);
    }

    [Fact]
    public void LerpOkLabUnclampedExtrapolates()
    {
        Color below = Color.LerpOkLabUnclamped(Color.Black, Color.White, -0.5f);
        Color above = Color.LerpOkLabUnclamped(Color.Black, Color.White, 1.5f);

        below.ToOkLab(out float belowL, out _, out _);
        above.ToOkLab(out float aboveL, out _, out _);

        AssertClose(-0.5f, belowL, 1e-4f);
        AssertClose(1.5f, aboveL, 1e-4f);
        AssertClose(-0.125f, below.R, 1e-4f);
        AssertClose(3.375f, above.R, 1e-4f);
    }

    [Fact]
    public void LerpOkLabInterpolatesAlphaLinearlyWithTheSameRatio()
    {
        Color a = new(0.2f, 0.6f, 0.4f, 0.2f);
        Color b = new(0.9f, 0.1f, 0.7f, 0.8f);

        AssertClose(0.2f, Color.LerpOkLab(a, b, 0f).A);
        AssertClose(0.35f, Color.LerpOkLab(a, b, 0.25f).A);
        AssertClose(0.5f, Color.LerpOkLab(a, b, 0.5f).A);
        AssertClose(0.8f, Color.LerpOkLab(a, b, 1f).A);
        AssertClose(-0.1f, Color.LerpOkLabUnclamped(a, b, -0.5f).A);
        AssertClose(1.1f, Color.LerpOkLabUnclamped(a, b, 1.5f).A);
    }

    private static void AssertClose(float expected, float actual, float tolerance = Tolerance)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= tolerance,
            $"Expected {expected} but got {actual} (tolerance {tolerance}).");
    }

    private static void AssertColorClose(Color expected, Color actual, float tolerance = Tolerance)
    {
        AssertClose(expected.R, actual.R, tolerance);
        AssertClose(expected.G, actual.G, tolerance);
        AssertClose(expected.B, actual.B, tolerance);
        AssertClose(expected.A, actual.A, tolerance);
    }
}
