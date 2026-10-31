using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The plain composite-alpha, pre-transform, and clipping decisions for one swapchain.
/// </summary>
/// <param name="CompositeAlpha">The selected composite alpha mode.</param>
/// <param name="PreTransform">The selected pre-transform.</param>
/// <param name="Clipped">Whether the surface is clipped to the swapchain extent.</param>
internal readonly record struct SwapchainSurfaceSelection(
    CompositeAlphaFlagsKHR CompositeAlpha,
    SurfaceTransformFlagsKHR PreTransform,
    bool Clipped);
