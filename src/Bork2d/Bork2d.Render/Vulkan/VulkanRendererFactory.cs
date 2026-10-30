using Bork2d.Engine;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Creates the production windowed Vulkan renderer; the host or tests inject the factory.
/// </summary>
/// <remarks>
/// The factory validates the windowed start request synchronously so a missing instance or surface
/// fails before a thread exists. It never starts the renderer and never touches the instance.
/// Offscreen starts are WP-4 and are rejected here for now.
/// </remarks>
internal sealed class VulkanRendererFactory : IRendererFactory
{
    /// <inheritdoc/>
    public IRenderer Create(RendererStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (startInfo.Mode != RendererMode.Windowed)
        {
            throw new Bork2dException(
                "Offscreen Vulkan rendering is not available in this build.");
        }

        if (startInfo.Instance is null)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-null Vulkan instance.");
        }

        if (startInfo.Instance.Handle == 0)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-zero Vulkan instance handle.");
        }

        if (startInfo.SurfaceHandle == 0)
        {
            throw new Bork2dException(
                "Windowed Vulkan rendering requires a non-zero surface handle.");
        }

        return new VulkanRenderer(startInfo);
    }
}
