namespace Bork2d.Render.Vulkan;

/// <summary>
/// The internal seam that creates the production instance, or a fake in tests.
/// </summary>
internal interface IVulkanInstanceFactory
{
    /// <summary>Creates a Vulkan instance for an explicit request.</summary>
    /// <param name="request">The validated creation request.</param>
    /// <returns>The created instance.</returns>
    IVulkanInstance Create(VulkanInstanceRequest request);
}
