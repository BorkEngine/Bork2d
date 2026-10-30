using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One offscreen format candidate plus whether it supports the color-attachment usage M6 needs
/// with optimal tiling.
/// </summary>
/// <param name="Format">The format being considered.</param>
/// <param name="SupportsOptimalTilingColorAttachment">
/// Whether <c>VK_FORMAT_FEATURE_COLOR_ATTACHMENT_BIT</c> is reported for optimal tiling.
/// </param>
internal readonly record struct OffscreenFormatSupport(
    Format Format,
    bool SupportsOptimalTilingColorAttachment);
