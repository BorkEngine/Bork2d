using Bork2d.Render;
using Bork2d.Render.Vulkan;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The immutable Vulkan window objects the OS/window thread publishes for the host (D-M6-09).
/// </summary>
/// <remarks>
/// The OS/window thread creates the instance and surface and destroys them at shutdown; the host
/// only copies the opaque handles into <see cref="RendererStartInfo"/>. The drawable size
/// (<c>SDL_Vulkan_GetDrawableSize</c>) is the extent authority for the swapchain. A replacement
/// snapshot exists only through the single D-M6-16 surface-loss recovery.
/// </remarks>
internal sealed class VulkanWindowSnapshot
{
    /// <summary>Initializes a new instance of the <see cref="VulkanWindowSnapshot"/> class.</summary>
    /// <param name="instance">The platform-owned Vulkan instance.</param>
    /// <param name="surfaceHandle">The opaque <c>VkSurfaceKHR</c> handle.</param>
    /// <param name="drawableSize">The drawable framebuffer size in pixels.</param>
    internal VulkanWindowSnapshot(
        IVulkanInstance instance,
        nint surfaceHandle,
        WindowExtent drawableSize)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Instance = instance;
        SurfaceHandle = surfaceHandle;
        DrawableSize = drawableSize;
    }

    /// <summary>Gets the platform-owned Vulkan instance.</summary>
    internal IVulkanInstance Instance { get; }

    /// <summary>Gets the opaque <c>VkSurfaceKHR</c> handle.</summary>
    internal nint SurfaceHandle { get; }

    /// <summary>Gets the drawable framebuffer size in pixels.</summary>
    internal WindowExtent DrawableSize { get; }
}
