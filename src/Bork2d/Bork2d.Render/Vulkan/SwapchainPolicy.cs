using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The pure swapchain extent, image-count, surface-property, and recreation policies.
/// </summary>
/// <remarks>
/// The windowed renderer (WP-3) consumes these values; WP-2 only supplies and tests them.
/// </remarks>
internal static class SwapchainPolicy
{
    /// <summary>Clamps a requested framebuffer extent to the surface's supported range.</summary>
    /// <param name="requested">The requested framebuffer extent in pixels.</param>
    /// <param name="minimum">The surface's minimum image extent.</param>
    /// <param name="maximum">The surface's maximum image extent.</param>
    /// <returns>The clamped extent.</returns>
    internal static Extent2D ClampExtent(Extent2D requested, Extent2D minimum, Extent2D maximum)
        => new()
        {
            Width = Math.Clamp(requested.Width, minimum.Width, maximum.Width),
            Height = Math.Clamp(requested.Height, minimum.Height, maximum.Height),
        };

    /// <summary>Gets whether an extent is zero in either dimension (minimized).</summary>
    /// <param name="extent">The framebuffer extent.</param>
    /// <returns><see langword="true"/> when acquire and present must be skipped.</returns>
    internal static bool IsZeroOrMinimized(Extent2D extent)
        => extent.Width == 0 || extent.Height == 0;

    /// <summary>
    /// Selects the swapchain image count: <c>minImageCount + 1</c>, clamped to
    /// <c>maxImageCount</c> when non-zero (D-M6-11).
    /// </summary>
    /// <param name="minImageCount">The surface minimum image count.</param>
    /// <param name="maxImageCount">The surface maximum; <c>0</c> means no limit.</param>
    /// <returns>The image count, never below <paramref name="minImageCount"/>.</returns>
    internal static uint SelectImageCount(uint minImageCount, uint maxImageCount)
    {
        var count = minImageCount == uint.MaxValue ? uint.MaxValue : minImageCount + 1;
        if (maxImageCount != 0)
        {
            count = Math.Min(count, maxImageCount);
        }

        return Math.Max(count, minImageCount);
    }

    /// <summary>
    /// Selects composite alpha, pre-transform, and clipping from the surface capabilities.
    /// </summary>
    /// <param name="capabilities">The reported surface capabilities.</param>
    /// <returns>The plain swapchain surface properties.</returns>
    internal static SwapchainSurfaceSelection SelectSurfaceProperties(
        SurfaceCapabilitiesKHR capabilities)
        => new(
            SelectCompositeAlpha(capabilities.SupportedCompositeAlpha),
            SelectPreTransform(capabilities.SupportedTransforms, capabilities.CurrentTransform),
            Clipped: true);

    /// <summary>
    /// Decides the next windowed frame-loop action from the requested extent, the current
    /// swapchain extent, the last acquire/present result, and the surface-recovery attempt state
    /// (D-M6-15, D-M6-16).
    /// </summary>
    /// <param name="requestedExtent">The latest requested framebuffer extent.</param>
    /// <param name="swapchainExtent">The current swapchain extent.</param>
    /// <param name="lastFrameResult">The last acquire or present result; <c>SUCCESS</c> otherwise.</param>
    /// <param name="surfaceRecoveryAttempted">
    /// Whether the single D-M6-16 recovery attempt has already been used.
    /// </param>
    /// <returns>The action the windowed renderer should take.</returns>
    internal static SwapchainAction DecideFrame(
        Extent2D requestedExtent,
        Extent2D swapchainExtent,
        Result lastFrameResult,
        bool surfaceRecoveryAttempted)
    {
        if (IsZeroOrMinimized(requestedExtent) || IsZeroOrMinimized(swapchainExtent))
        {
            return SwapchainAction.SkipMinimized;
        }

        if (lastFrameResult == Result.ErrorDeviceLost)
        {
            return SwapchainAction.Fatal;
        }

        if (lastFrameResult == Result.ErrorSurfaceLostKhr)
        {
            return surfaceRecoveryAttempted ? SwapchainAction.Fatal : SwapchainAction.RecoverSurface;
        }

        if (lastFrameResult == Result.ErrorOutOfDateKhr)
        {
            return SwapchainAction.RecreateAndRetry;
        }

        // SUBOPTIMAL presents the current frame first and recreates on the next iteration
        // (D-M6-15), even when the requested extent already changed; the next iteration then
        // recreates on the extent difference.
        if (lastFrameResult == Result.SuboptimalKhr)
        {
            return SwapchainAction.PresentThenRecreate;
        }

        if (requestedExtent.Width != swapchainExtent.Width
            || requestedExtent.Height != swapchainExtent.Height)
        {
            return SwapchainAction.RecreateAndRetry;
        }

        return lastFrameResult == Result.Success ? SwapchainAction.Present : SwapchainAction.Fatal;
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(
        CompositeAlphaFlagsKHR supported)
    {
        if (Has(supported, CompositeAlphaFlagsKHR.OpaqueBitKhr))
        {
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;
        }

        if (Has(supported, CompositeAlphaFlagsKHR.PreMultipliedBitKhr))
        {
            return CompositeAlphaFlagsKHR.PreMultipliedBitKhr;
        }

        if (Has(supported, CompositeAlphaFlagsKHR.PostMultipliedBitKhr))
        {
            return CompositeAlphaFlagsKHR.PostMultipliedBitKhr;
        }

        return Has(supported, CompositeAlphaFlagsKHR.InheritBitKhr)
            ? CompositeAlphaFlagsKHR.InheritBitKhr
            : CompositeAlphaFlagsKHR.None;
    }

    private static SurfaceTransformFlagsKHR SelectPreTransform(
        SurfaceTransformFlagsKHR supported,
        SurfaceTransformFlagsKHR current)
    {
        if (current != SurfaceTransformFlagsKHR.None && Has(supported, current))
        {
            return current;
        }

        if (Has(supported, SurfaceTransformFlagsKHR.IdentityBitKhr))
        {
            return SurfaceTransformFlagsKHR.IdentityBitKhr;
        }

        SurfaceTransformFlagsKHR[] known =
        [
            SurfaceTransformFlagsKHR.Rotate90BitKhr,
            SurfaceTransformFlagsKHR.Rotate180BitKhr,
            SurfaceTransformFlagsKHR.Rotate270BitKhr,
            SurfaceTransformFlagsKHR.HorizontalMirrorBitKhr,
            SurfaceTransformFlagsKHR.HorizontalMirrorRotate90BitKhr,
            SurfaceTransformFlagsKHR.HorizontalMirrorRotate180BitKhr,
            SurfaceTransformFlagsKHR.HorizontalMirrorRotate270BitKhr,
            SurfaceTransformFlagsKHR.InheritBitKhr,
        ];

        foreach (SurfaceTransformFlagsKHR candidate in known)
        {
            if (Has(supported, candidate))
            {
                return candidate;
            }
        }

        return SurfaceTransformFlagsKHR.None;
    }

    private static bool Has(SurfaceTransformFlagsKHR supported, SurfaceTransformFlagsKHR flag)
        => (supported & flag) == flag;

    private static bool Has(CompositeAlphaFlagsKHR supported, CompositeAlphaFlagsKHR flag)
        => (supported & flag) == flag;
}
