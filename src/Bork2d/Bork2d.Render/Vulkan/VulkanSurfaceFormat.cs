using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One surface format/color-space pair reported by the surface capabilities.
/// </summary>
/// <param name="Format">The reported image format.</param>
/// <param name="ColorSpace">The reported color space.</param>
internal readonly record struct VulkanSurfaceFormat(Format Format, ColorSpaceKHR ColorSpace);
