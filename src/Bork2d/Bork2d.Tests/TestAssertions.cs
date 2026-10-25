using System.Numerics;
using Xunit;

namespace Bork2d.Tests;

internal static class TestAssertions
{
    private const float Tolerance = 1e-4f;

    internal static void Close(float expected, float actual, string? details = null)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= Tolerance,
            details ?? $"Expected {expected}, got {actual}.");
    }

    internal static void Close(Vector2 expected, Vector2 actual, string? details = null)
    {
        Assert.True(
            Vector2.Distance(expected, actual) <= Tolerance,
            details ?? $"Expected {expected}, got {actual}.");
    }

    internal static void Close(Matrix3x2 expected, Matrix3x2 actual)
    {
        Close(expected.M11, actual.M11, $"M11: expected {expected.M11}, got {actual.M11}.");
        Close(expected.M12, actual.M12, $"M12: expected {expected.M12}, got {actual.M12}.");
        Close(expected.M21, actual.M21, $"M21: expected {expected.M21}, got {actual.M21}.");
        Close(expected.M22, actual.M22, $"M22: expected {expected.M22}, got {actual.M22}.");
        Close(expected.M31, actual.M31, $"M31: expected {expected.M31}, got {actual.M31}.");
        Close(expected.M32, actual.M32, $"M32: expected {expected.M32}, got {actual.M32}.");
    }
}
