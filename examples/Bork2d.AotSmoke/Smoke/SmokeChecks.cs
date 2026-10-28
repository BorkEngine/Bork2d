namespace Bork2d.AotSmoke.Smoke;

/// <summary>
/// Throws a diagnostic exception when an AOT smoke expectation fails.
/// </summary>
internal static class SmokeChecks
{
    /// <summary>
    /// Requires <paramref name="condition"/> to be <see langword="true"/>.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="message">The failure diagnostic.</param>
    /// <exception cref="InvalidOperationException">The condition is <see langword="false"/>.</exception>
    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"AOT smoke check failed: {message}");
        }
    }

    /// <summary>
    /// Requires two values to be reference-equal.
    /// </summary>
    /// <param name="expected">The expected instance.</param>
    /// <param name="actual">The observed instance.</param>
    /// <param name="message">The failure diagnostic.</param>
    internal static void RequireSame(object? expected, object? actual, string message)
        => Require(ReferenceEquals(expected, actual), message);
}
