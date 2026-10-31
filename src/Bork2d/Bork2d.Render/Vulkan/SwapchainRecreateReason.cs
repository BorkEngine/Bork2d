namespace Bork2d.Render.Vulkan;

/// <summary>
/// Why the windowed renderer recreated its swapchain (D-M6-15, D-M6-16).
/// </summary>
internal enum SwapchainRecreateReason
{
    /// <summary>The framebuffer extent changed.</summary>
    Resize,

    /// <summary>The fullscreen state changed.</summary>
    Fullscreen,

    /// <summary>Acquire or present reported <c>VK_ERROR_OUT_OF_DATE_KHR</c>.</summary>
    OutOfDate,

    /// <summary>Acquire or present reported <c>VK_SUBOPTIMAL_KHR</c>.</summary>
    Suboptimal,

    /// <summary>The single D-M6-16 surface-loss recovery replaced the surface.</summary>
    SurfaceLost,
}
