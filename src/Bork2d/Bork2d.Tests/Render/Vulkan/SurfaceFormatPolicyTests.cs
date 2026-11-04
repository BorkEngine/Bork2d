using Bork2d.Engine;
using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for <see cref="SurfaceFormatPolicy"/>: every target is UNORM and the final
/// present pass owns the sRGB encoding (D-M6-04, D-M6-26).
/// </summary>
public sealed class SurfaceFormatPolicyTests
{
    [Fact]
    public void WindowedPrefersBgraUnormOverRgbaUnorm()
    {
        VulkanSurfaceFormat selected = SurfaceFormatPolicy.SelectWindowed(
        [
            new VulkanSurfaceFormat(Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new VulkanSurfaceFormat(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ]);

        Assert.Equal(Format.B8G8R8A8Unorm, selected.Format);
        Assert.Equal(ColorSpaceKHR.SpaceSrgbNonlinearKhr, selected.ColorSpace);
    }

    [Fact]
    public void WindowedFallsBackToRgbaUnormWhenBgraIsAbsent()
    {
        VulkanSurfaceFormat selected = SurfaceFormatPolicy.SelectWindowed(
        [
            new VulkanSurfaceFormat(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new VulkanSurfaceFormat(Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ]);

        Assert.Equal(Format.R8G8B8A8Unorm, selected.Format);
    }

    [Fact]
    public void WindowedIgnoresUnormFormatsWithTheWrongColorSpace()
    {
        Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectWindowed(
            [
                new VulkanSurfaceFormat(
                    Format.B8G8R8A8Unorm,
                    ColorSpaceKHR.SpaceDisplayP3NonlinearExt),
            ]));
    }

    [Fact]
    public void WindowedNeverFallsBackToSrgbFormats()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectWindowed(
            [
                new VulkanSurfaceFormat(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
                new VulkanSurfaceFormat(Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            ]));

        Assert.Contains("B8G8R8A8Srgb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("R8G8B8A8Srgb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UNORM", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OffscreenPrefersRgbaUnormOverBgraUnorm()
    {
        Format selected = SurfaceFormatPolicy.SelectOffscreen(
        [
            new OffscreenFormatSupport(Format.B8G8R8A8Unorm, SupportsOptimalTilingColorAttachment: true),
            new OffscreenFormatSupport(Format.R8G8B8A8Unorm, SupportsOptimalTilingColorAttachment: true),
        ]);

        Assert.Equal(Format.R8G8B8A8Unorm, selected);
    }

    [Fact]
    public void OffscreenIgnoresFormatsWithoutOptimalTilingColorAttachment()
    {
        Format selected = SurfaceFormatPolicy.SelectOffscreen(
        [
            new OffscreenFormatSupport(Format.R8G8B8A8Unorm, SupportsOptimalTilingColorAttachment: false),
            new OffscreenFormatSupport(Format.B8G8R8A8Unorm, SupportsOptimalTilingColorAttachment: true),
        ]);

        Assert.Equal(Format.B8G8R8A8Unorm, selected);
    }

    [Fact]
    public void OffscreenNeverFallsBackToSrgbFormats()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectOffscreen(
            [
                new OffscreenFormatSupport(Format.R8G8B8A8Srgb, SupportsOptimalTilingColorAttachment: true),
                new OffscreenFormatSupport(Format.B8G8R8A8Srgb, SupportsOptimalTilingColorAttachment: true),
            ]));

        Assert.Contains("R8G8B8A8Srgb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("B8G8R8A8Srgb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UNORM", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OffscreenFailureListsTheEnumeratedFormats()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectOffscreen(
            [
                new OffscreenFormatSupport(Format.R8G8B8A8Srgb, SupportsOptimalTilingColorAttachment: true),
            ]));

        Assert.Contains("R8G8B8A8Srgb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UNORM", exception.Message, StringComparison.Ordinal);
    }
}
