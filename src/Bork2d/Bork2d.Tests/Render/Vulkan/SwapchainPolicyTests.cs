using Bork2d.Render.Vulkan;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for <see cref="SwapchainPolicy"/> (D-M6-11, D-M6-15, D-M6-16).
/// </summary>
public sealed class SwapchainPolicyTests
{
    private static readonly Extent2D _swapchainExtent = new() { Width = 1280, Height = 720 };

    [Fact]
    public void ClampExtentKeepsAnInRangeRequest()
    {
        Extent2D clamped = SwapchainPolicy.ClampExtent(
            new Extent2D { Width = 800, Height = 600 },
            new Extent2D { Width = 320, Height = 240 },
            new Extent2D { Width = 3840, Height = 2160 });

        Assert.Equal(800u, clamped.Width);
        Assert.Equal(600u, clamped.Height);
    }

    [Fact]
    public void ClampExtentRaisesARequestBelowTheMinimum()
    {
        Extent2D clamped = SwapchainPolicy.ClampExtent(
            new Extent2D { Width = 0, Height = 10 },
            new Extent2D { Width = 320, Height = 240 },
            new Extent2D { Width = 3840, Height = 2160 });

        Assert.Equal(320u, clamped.Width);
        Assert.Equal(240u, clamped.Height);
    }

    [Fact]
    public void ClampExtentLowersARequestAboveTheMaximum()
    {
        Extent2D clamped = SwapchainPolicy.ClampExtent(
            new Extent2D { Width = 7680, Height = 4320 },
            new Extent2D { Width = 320, Height = 240 },
            new Extent2D { Width = 3840, Height = 2160 });

        Assert.Equal(3840u, clamped.Width);
        Assert.Equal(2160u, clamped.Height);
    }

    [Fact]
    public void ZeroExtentIsMinimized()
    {
        Assert.True(SwapchainPolicy.IsZeroOrMinimized(default));
        Assert.True(
            SwapchainPolicy.IsZeroOrMinimized(new Extent2D { Width = 0, Height = 720 }));
        Assert.True(
            SwapchainPolicy.IsZeroOrMinimized(new Extent2D { Width = 1280, Height = 0 }));
        Assert.False(SwapchainPolicy.IsZeroOrMinimized(_swapchainExtent));
    }

    [Fact]
    public void ImageCountAddsOneToTheMinimum()
    {
        Assert.Equal(3u, SwapchainPolicy.SelectImageCount(minImageCount: 2, maxImageCount: 0));
    }

    [Fact]
    public void ImageCountClampsToAMaximum()
    {
        Assert.Equal(2u, SwapchainPolicy.SelectImageCount(minImageCount: 2, maxImageCount: 2));
        Assert.Equal(4u, SwapchainPolicy.SelectImageCount(minImageCount: 4, maxImageCount: 4));
    }

    [Fact]
    public void ImageCountNeverFallsBelowTheMinimum()
    {
        Assert.Equal(
            5u,
            SwapchainPolicy.SelectImageCount(minImageCount: 5, maxImageCount: 3));
    }

    [Fact]
    public void ImageCountDoesNotOverflowAtTheUintMaximum()
    {
        Assert.Equal(uint.MaxValue, SwapchainPolicy.SelectImageCount(uint.MaxValue, 0));
    }

    [Fact]
    public void SurfacePropertiesUseOpaqueAndTheCurrentTransform()
    {
        var capabilities = new SurfaceCapabilitiesKHR
        {
            SupportedCompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr
                | CompositeAlphaFlagsKHR.InheritBitKhr,
            SupportedTransforms = SurfaceTransformFlagsKHR.IdentityBitKhr
                | SurfaceTransformFlagsKHR.Rotate90BitKhr,
            CurrentTransform = SurfaceTransformFlagsKHR.Rotate90BitKhr,
        };

        SwapchainSurfaceSelection selection = SwapchainPolicy.SelectSurfaceProperties(capabilities);

        Assert.Equal(CompositeAlphaFlagsKHR.OpaqueBitKhr, selection.CompositeAlpha);
        Assert.Equal(SurfaceTransformFlagsKHR.Rotate90BitKhr, selection.PreTransform);
        Assert.True(selection.Clipped);
    }

    [Fact]
    public void SurfacePropertiesFallBackToIdentityTransform()
    {
        var capabilities = new SurfaceCapabilitiesKHR
        {
            SupportedCompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            SupportedTransforms = SurfaceTransformFlagsKHR.IdentityBitKhr,
            CurrentTransform = SurfaceTransformFlagsKHR.Rotate90BitKhr,
        };

        SwapchainSurfaceSelection selection = SwapchainPolicy.SelectSurfaceProperties(capabilities);

        Assert.Equal(SurfaceTransformFlagsKHR.IdentityBitKhr, selection.PreTransform);
    }

    [Fact]
    public void SurfacePropertiesFallBackToPreMultipliedAlpha()
    {
        var capabilities = new SurfaceCapabilitiesKHR
        {
            SupportedCompositeAlpha = CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
            SupportedTransforms = SurfaceTransformFlagsKHR.IdentityBitKhr,
            CurrentTransform = SurfaceTransformFlagsKHR.IdentityBitKhr,
        };

        SwapchainSurfaceSelection selection = SwapchainPolicy.SelectSurfaceProperties(capabilities);

        Assert.Equal(CompositeAlphaFlagsKHR.PreMultipliedBitKhr, selection.CompositeAlpha);
    }

    [Fact]
    public void DecideFrameSkipsAcquireAndPresentWhenMinimized()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            new Extent2D { Width = 0, Height = 0 },
            _swapchainExtent,
            Result.Success,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.SkipMinimized, action);
    }

    [Fact]
    public void DecideFramePresentsACleanFrame()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.Success,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.Present, action);
    }

    [Fact]
    public void DecideFrameRecreatesAndRetriesOnOutOfDate()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.ErrorOutOfDateKhr,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.RecreateAndRetry, action);
    }

    [Fact]
    public void DecideFrameRecreatesAndRetriesWhenTheExtentChanged()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            new Extent2D { Width = 1600, Height = 900 },
            _swapchainExtent,
            Result.Success,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.RecreateAndRetry, action);
    }

    [Fact]
    public void DecideFrameRecreatesAndRetriesOnOutOfDateEvenWhenTheExtentChanged()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            new Extent2D { Width = 1600, Height = 900 },
            _swapchainExtent,
            Result.ErrorOutOfDateKhr,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.RecreateAndRetry, action);
    }

    [Fact]
    public void DecideFramePresentsThenRecreatesOnSuboptimal()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.SuboptimalKhr,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.PresentThenRecreate, action);
    }

    [Fact]
    public void DecideFramePresentsThenRecreatesWhenSuboptimalAlsoChangedTheExtent()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            new Extent2D { Width = 1600, Height = 900 },
            _swapchainExtent,
            Result.SuboptimalKhr,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.PresentThenRecreate, action);
    }

    [Fact]
    public void DecideFrameRecoversTheFirstSurfaceLoss()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.ErrorSurfaceLostKhr,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.RecoverSurface, action);
    }

    [Fact]
    public void DecideFrameTreatsASecondSurfaceLossAsFatal()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.ErrorSurfaceLostKhr,
            surfaceRecoveryAttempted: true);

        Assert.Equal(SwapchainAction.Fatal, action);
    }

    [Fact]
    public void DecideFrameTreatsDeviceLossAsFatal()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.ErrorDeviceLost,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.Fatal, action);
    }

    [Fact]
    public void DecideFrameTreatsUnexpectedErrorsAsFatal()
    {
        SwapchainAction action = SwapchainPolicy.DecideFrame(
            _swapchainExtent,
            _swapchainExtent,
            Result.ErrorOutOfDeviceMemory,
            surfaceRecoveryAttempted: false);

        Assert.Equal(SwapchainAction.Fatal, action);
    }
}
