namespace Bork2d.Render.Vulkan;

/// <summary>
/// The device limits M6 logs and validates for later work packages.
/// </summary>
/// <param name="MaxImageDimension2D">The maximum 2D image dimension.</param>
/// <param name="MaxImageArrayLayers">The maximum image array layers.</param>
/// <param name="MaxColorAttachments">The maximum color attachments per framebuffer.</param>
/// <param name="MaxFramebufferWidth">The maximum framebuffer width.</param>
/// <param name="MaxFramebufferHeight">The maximum framebuffer height.</param>
/// <param name="MaxPerStageDescriptorUpdateAfterBindSampledImages">
/// The update-after-bind sampled-image capacity used for M8 bindless page planning.
/// </param>
internal readonly record struct VulkanDeviceLimits(
    uint MaxImageDimension2D,
    uint MaxImageArrayLayers,
    uint MaxColorAttachments,
    uint MaxFramebufferWidth,
    uint MaxFramebufferHeight,
    uint MaxPerStageDescriptorUpdateAfterBindSampledImages);
