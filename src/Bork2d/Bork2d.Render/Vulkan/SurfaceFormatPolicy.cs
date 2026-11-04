using System.Text;
using Bork2d.Engine;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The pure target-format selection policy: every target is a UNORM image and the sRGB encoding is
/// the final present pass's job.
/// </summary>
/// <remarks>
/// The 2D passes render linear color into a linear scene image, and the final present pass applies the
/// sRGB opto-electronic transfer function when it writes the target. The target therefore must not be
/// an sRGB format, or the encoding would be applied twice. A missing UNORM format is a startup
/// failure naming the enumerated candidates so the driver's actual format list reaches the log.
/// </remarks>
internal static class SurfaceFormatPolicy
{
    /// <summary>
    /// Selects the windowed surface format: <c>B8G8R8A8_UNORM</c>, then <c>R8G8B8A8_UNORM</c>, always
    /// with <c>VK_COLOR_SPACE_SRGB_NONLINEAR_KHR</c>.
    /// </summary>
    /// <param name="available">The formats reported for the surface.</param>
    /// <returns>The selected format and color space.</returns>
    /// <exception cref="Bork2dException">
    /// No supported UNORM format/color-space pair is available.
    /// </exception>
    internal static VulkanSurfaceFormat SelectWindowed(IReadOnlyList<VulkanSurfaceFormat> available)
    {
        if (TryFindWindowed(available, Format.B8G8R8A8Unorm, out VulkanSurfaceFormat selected)
            || TryFindWindowed(available, Format.R8G8B8A8Unorm, out selected))
        {
            return selected;
        }

        throw new Bork2dException(
            "No UNORM windowed surface format is available. Supported UNORM formats use "
                + "VK_COLOR_SPACE_SRGB_NONLINEAR_KHR; enumerated formats: "
                + $"{DescribeWindowed(available)}.");
    }

    /// <summary>
    /// Selects the offscreen color format: <c>R8G8B8A8_UNORM</c>, then <c>B8G8R8A8_UNORM</c>, among
    /// formats that support color attachment with optimal tiling.
    /// </summary>
    /// <param name="available">The format candidates and their optimal-tiling support.</param>
    /// <returns>The selected format.</returns>
    /// <exception cref="Bork2dException">No supported UNORM format is available.</exception>
    internal static Format SelectOffscreen(IReadOnlyList<OffscreenFormatSupport> available)
    {
        if (TryFindOffscreen(available, Format.R8G8B8A8Unorm, out Format selected)
            || TryFindOffscreen(available, Format.B8G8R8A8Unorm, out selected))
        {
            return selected;
        }

        throw new Bork2dException(
            "No UNORM offscreen color format is available with optimal tiling and color-attachment "
                + $"usage; enumerated formats: {DescribeOffscreen(available)}.");
    }

    private static bool TryFindWindowed(
        IReadOnlyList<VulkanSurfaceFormat> available,
        Format format,
        out VulkanSurfaceFormat selected)
    {
        for (var index = 0; index < available.Count; index++)
        {
            VulkanSurfaceFormat candidate = available[index];
            if (candidate.Format == format
                && candidate.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                selected = candidate;
                return true;
            }
        }

        selected = default;
        return false;
    }

    private static bool TryFindOffscreen(
        IReadOnlyList<OffscreenFormatSupport> available,
        Format format,
        out Format selected)
    {
        for (var index = 0; index < available.Count; index++)
        {
            OffscreenFormatSupport candidate = available[index];
            if (candidate.Format == format && candidate.SupportsOptimalTilingColorAttachment)
            {
                selected = candidate.Format;
                return true;
            }
        }

        selected = default;
        return false;
    }

    private static string DescribeWindowed(IReadOnlyList<VulkanSurfaceFormat> available)
    {
        if (available.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        for (var index = 0; index < available.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            VulkanSurfaceFormat format = available[index];
            builder.Append(format.Format).Append('/').Append(format.ColorSpace);
        }

        return builder.ToString();
    }

    private static string DescribeOffscreen(IReadOnlyList<OffscreenFormatSupport> available)
    {
        if (available.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        for (var index = 0; index < available.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(available[index].Format);
        }

        return builder.ToString();
    }
}
