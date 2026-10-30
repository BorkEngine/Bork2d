using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The Vulkan device extensions and layer names M6 uses.
/// </summary>
internal static class VulkanNames
{
    /// <summary>The Khronos validation layer.</summary>
    internal const string ValidationLayer = "VK_LAYER_KHRONOS_validation";

    /// <summary>The <c>VK_KHR_surface</c> instance extension.</summary>
    internal const string SurfaceExtension = KhrSurface.ExtensionName;

    /// <summary>The <c>VK_KHR_swapchain</c> device extension.</summary>
    internal const string SwapchainExtension = KhrSwapchain.ExtensionName;

    /// <summary>The <c>VK_KHR_dynamic_rendering</c> device extension.</summary>
    internal const string DynamicRenderingExtension = KhrDynamicRendering.ExtensionName;

    /// <summary>The <c>VK_KHR_synchronization2</c> device extension.</summary>
    internal const string Synchronization2Extension = KhrSynchronization2.ExtensionName;

    /// <summary>The <c>VK_EXT_debug_utils</c> instance extension.</summary>
    internal const string DebugUtilsExtension = ExtDebugUtils.ExtensionName;
}
