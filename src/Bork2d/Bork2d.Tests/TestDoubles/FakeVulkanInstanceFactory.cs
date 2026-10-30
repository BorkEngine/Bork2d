using Bork2d.Render.Vulkan;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// Provides a <see cref="FakeVulkanInstance"/> and records the explicit creation request.
/// </summary>
internal sealed class FakeVulkanInstanceFactory : IVulkanInstanceFactory
{
    /// <summary>Gets the fake instance returned by <see cref="Create"/>.</summary>
    internal FakeVulkanInstance Instance { get; } = new();

    /// <summary>Gets the last creation request.</summary>
    internal VulkanInstanceRequest? LastRequest { get; private set; }

    /// <summary>Gets the number of create calls.</summary>
    internal int CreateCount { get; private set; }

    /// <inheritdoc/>
    public IVulkanInstance Create(VulkanInstanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CreateCount++;
        LastRequest = request;
        return Instance;
    }
}
