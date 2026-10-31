using Xunit;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Serializes the opt-in real-GPU tests so only one Vulkan instance/device is alive at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealGpuCollection
{
    /// <summary>The collection name used by every <see cref="GpuFactAttribute"/> test class.</summary>
    internal const string Name = "RealGpu";
}
