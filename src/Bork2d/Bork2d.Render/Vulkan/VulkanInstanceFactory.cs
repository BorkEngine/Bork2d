namespace Bork2d.Render.Vulkan;

/// <summary>
/// The production instance factory; the only place <see cref="VulkanInstance"/> is constructed.
/// </summary>
internal sealed class VulkanInstanceFactory : IVulkanInstanceFactory
{
    /// <inheritdoc/>
    public IVulkanInstance Create(VulkanInstanceRequest request)
        => VulkanInstance.Create(request);
}
