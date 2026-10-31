using Bork2d.Engine;
using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for <see cref="SurfaceFormatPolicy"/> (D-M6-04, D-M6-26).
/// </summary>
public sealed class SurfaceFormatPolicyTests
{
    [Fact]
    public void WindowedPrefersBgraSrgbOverRgbaSrgb()
    {
        VulkanSurfaceFormat selected = SurfaceFormatPolicy.SelectWindowed(
        [
            new VulkanSurfaceFormat(Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new VulkanSurfaceFormat(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ]);

        Assert.Equal(Format.B8G8R8A8Srgb, selected.Format);
        Assert.Equal(ColorSpaceKHR.SpaceSrgbNonlinearKhr, selected.ColorSpace);
    }

    [Fact]
    public void WindowedFallsBackToRgbaSrgbWhenBgraIsAbsent()
    {
        VulkanSurfaceFormat selected = SurfaceFormatPolicy.SelectWindowed(
        [
            new VulkanSurfaceFormat(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            new VulkanSurfaceFormat(Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
        ]);

        Assert.Equal(Format.R8G8B8A8Srgb, selected.Format);
    }

    [Fact]
    public void WindowedIgnoresSrgbFormatsWithTheWrongColorSpace()
    {
        Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectWindowed(
            [
                new VulkanSurfaceFormat(
                    Format.B8G8R8A8Srgb,
                    ColorSpaceKHR.SpaceDisplayP3NonlinearExt),
            ]));
    }

    [Fact]
    public void WindowedFailureListsTheEnumeratedFormats()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectWindowed(
            [
                new VulkanSurfaceFormat(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
                new VulkanSurfaceFormat(Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            ]));

        Assert.Contains("B8G8R8A8Unorm", exception.Message, StringComparison.Ordinal);
        Assert.Contains("R8G8B8A8Unorm", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sRGB", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OffscreenPrefersRgbaSrgbOverBgraSrgb()
    {
        Format selected = SurfaceFormatPolicy.SelectOffscreen(
        [
            new OffscreenFormatSupport(Format.B8G8R8A8Srgb, SupportsOptimalTilingColorAttachment: true),
            new OffscreenFormatSupport(Format.R8G8B8A8Srgb, SupportsOptimalTilingColorAttachment: true),
        ]);

        Assert.Equal(Format.R8G8B8A8Srgb, selected);
    }

    [Fact]
    public void OffscreenIgnoresFormatsWithoutOptimalTilingColorAttachment()
    {
        Format selected = SurfaceFormatPolicy.SelectOffscreen(
        [
            new OffscreenFormatSupport(Format.R8G8B8A8Srgb, SupportsOptimalTilingColorAttachment: false),
            new OffscreenFormatSupport(Format.B8G8R8A8Srgb, SupportsOptimalTilingColorAttachment: true),
        ]);

        Assert.Equal(Format.B8G8R8A8Srgb, selected);
    }

    [Fact]
    public void OffscreenFailureListsTheEnumeratedFormats()
    {
        var exception = Assert.Throws<Bork2dException>(
            () => SurfaceFormatPolicy.SelectOffscreen(
            [
                new OffscreenFormatSupport(Format.R8G8B8A8Unorm, SupportsOptimalTilingColorAttachment: true),
            ]));

        Assert.Contains("R8G8B8A8Unorm", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sRGB", exception.Message, StringComparison.Ordinal);
    }
}
