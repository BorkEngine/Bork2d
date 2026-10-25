using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Marks a test that needs a real SDL window and a desktop session.
/// </summary>
/// <remarks>
/// The test is skipped unless the <c>BORK2D_RUN_WINDOW_TESTS</c> environment variable is <c>1</c>,
/// so CI never initializes SDL video or opens a window. No extra test package is involved: the
/// attribute sets xUnit's built-in <see cref="FactAttribute.Skip"/>.
/// </remarks>
public sealed class WindowFactAttribute : FactAttribute
{
    /// <summary>The environment variable that enables real-window tests.</summary>
    internal const string EnvironmentVariable = "BORK2D_RUN_WINDOW_TESTS";

    /// <summary>Initializes a new instance of the <see cref="WindowFactAttribute"/> class.</summary>
    public WindowFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = $"Set {EnvironmentVariable}=1 and run on a desktop session to run real-window "
                + "SDL tests.";
        }
    }

    /// <summary>Gets a value indicating whether real-window tests are enabled.</summary>
    /// <returns><see langword="true"/> when the environment variable is exactly <c>1</c>.</returns>
    internal static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            "1",
            StringComparison.Ordinal);
}
