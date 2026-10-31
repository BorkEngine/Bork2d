namespace Bork2d.Render.Vulkan;

/// <summary>
/// The device limits M6 logs and the M8 renderer validates for texture pages and instance storage.
/// </summary>
/// <param name="MaxImageDimension2D">The maximum 2D image dimension.</param>
/// <param name="MaxImageArrayLayers">The maximum image array layers.</param>
/// <param name="MaxColorAttachments">The maximum color attachments per framebuffer.</param>
/// <param name="MaxFramebufferWidth">The maximum framebuffer width.</param>
/// <param name="MaxFramebufferHeight">The maximum framebuffer height.</param>
/// <param name="MaxPerStageDescriptorUpdateAfterBindSampledImages">
/// The per-stage update-after-bind sampled-image limit used for M8 bindless page capacity.
/// </param>
/// <param name="MaxDescriptorSetUpdateAfterBindSampledImages">
/// The per-set update-after-bind sampled-image limit; the effective page limit is the smaller of
/// this and <paramref name="MaxPerStageDescriptorUpdateAfterBindSampledImages"/>.
/// </param>
/// <param name="MaxStorageBufferRange">The maximum storage-buffer range (the instance buffer check).</param>
internal readonly record struct VulkanDeviceLimits(
    uint MaxImageDimension2D,
    uint MaxImageArrayLayers,
    uint MaxColorAttachments,
    uint MaxFramebufferWidth,
    uint MaxFramebufferHeight,
    uint MaxPerStageDescriptorUpdateAfterBindSampledImages,
    uint MaxDescriptorSetUpdateAfterBindSampledImages,
    uint MaxStorageBufferRange);
