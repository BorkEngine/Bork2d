using Xunit;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Marks a test that needs a real Vulkan 1.3 device and loader.
/// </summary>
/// <remarks>
/// The test is skipped unless the <c>BORK2D_RUN_GPU_TESTS</c> environment variable is <c>1</c>, so
/// CI never loads <c>vulkan-1.dll</c>. No extra test package is involved: the attribute sets
/// xUnit's built-in <see cref="FactAttribute.Skip"/>. The tests are further serialized in
/// <see cref="RealGpuCollection"/> and skip at runtime when the loader, a suitable device, or the
/// validation layer is unavailable (D-M6-05).
/// </remarks>
public sealed class GpuFactAttribute : FactAttribute
{
    /// <summary>The environment variable that enables real-GPU tests.</summary>
    internal const string EnvironmentVariable = "BORK2D_RUN_GPU_TESTS";

    /// <summary>Initializes a new instance of the <see cref="GpuFactAttribute"/> class.</summary>
    public GpuFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = $"Set {EnvironmentVariable}=1 and run on a Vulkan 1.3 machine to run real-GPU "
                + "tests.";
        }
    }

    /// <summary>Gets a value indicating whether real-GPU tests are enabled.</summary>
    /// <returns><see langword="true"/> when the environment variable is exactly <c>1</c>.</returns>
    internal static bool IsEnabled()
        => string.Equals(
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            "1",
            StringComparison.Ordinal);
}
