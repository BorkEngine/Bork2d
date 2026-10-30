using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the WP-2 instance/device foundation.
/// </summary>
/// <remarks>
/// The test creates a validation-enabled instance and selects an offscreen device, then asserts
/// the chosen device/features/extensions and zero validation errors/warnings at clean shutdown.
/// It skips (rather than fails) when the loader, Vulkan 1.3 device, or validation layer is
/// unavailable (D-M6-05, D-M6-20). No surface or swapchain is created in WP-2.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class GpuDeviceBootstrapTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="GpuDeviceBootstrapTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public GpuDeviceBootstrapTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void OffscreenInstanceAndDeviceSelectionAreValidationClean()
    {
        if (!NativeLibrary.TryLoad("vulkan-1.dll", out nint loaderHandle))
        {
            throw SkipException.ForSkip("The Vulkan loader vulkan-1.dll is not available.");
        }

        NativeLibrary.Free(loaderHandle);

        var provider = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(provider);
        });

        IVulkanInstance? instance = null;
        VulkanDevice? device = null;
        try
        {
            try
            {
                instance = new VulkanInstanceFactory().Create(
                    new VulkanInstanceRequest
                    {
                        RequiredExtensions = [],
                        ValidationRequested = true,
                        ApplicationName = "Bork2d GPU test",
                        EngineName = "Bork2d",
                        LoggerFactory = loggerFactory,
                    });
            }
            catch (Bork2dException exception) when (IsUnavailable(exception))
            {
                throw SkipException.ForSkip(
                    $"A Vulkan 1.3 loader/instance is not available: {exception.Message}");
            }

            if (!instance.ValidationEnabled)
            {
                throw SkipException.ForSkip(
                    $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot "
                        + "be gathered on this machine.");
            }

            try
            {
                device = VulkanDeviceSelector.Select(
                    instance,
                    requirePresent: false,
                    surfaceHandle: 0,
                    loggerFactory.CreateLogger(RenderLog.Category));
            }
            catch (Bork2dException exception) when (IsUnavailable(exception))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 device is available: {exception.Message}");
            }

            Assert.Equal(VulkanFeatureSet.Required, device.Features);
            Assert.True(device.Features.DynamicRendering);
            Assert.True(device.Features.Synchronization2);
            Assert.True(device.Features.RuntimeDescriptorArray);
            Assert.True(device.Features.ShaderSampledImageArrayNonUniformIndexing);
            Assert.True(device.Features.DescriptorBindingPartiallyBound);
            Assert.True(device.Features.DescriptorBindingSampledImageUpdateAfterBind);
            Assert.True(device.Features.DescriptorBindingUpdateUnusedWhilePending);
            Assert.True(device.Features.DescriptorBindingVariableDescriptorCount);

            Assert.NotEqual(nint.Zero, device.Device.Handle);
            Assert.NotEqual(nint.Zero, device.Queue.Handle);
            Assert.DoesNotContain(
                VulkanNames.SwapchainExtension,
                device.EnabledExtensions,
                StringComparer.Ordinal);
            Assert.True(device.Limits.MaxImageDimension2D > 0);
            Assert.True(device.Limits.MaxImageArrayLayers > 0);
            Assert.True(device.Limits.MaxColorAttachments > 0);
            Assert.True(device.Limits.MaxFramebufferWidth > 0);
            Assert.True(device.Limits.MaxFramebufferHeight > 0);
            Assert.True(device.Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages > 0);

            Assert.Contains(
                VulkanNames.ValidationLayer,
                instance.EnabledLayers,
                StringComparer.Ordinal);
            Assert.Contains(
                VulkanNames.DebugUtilsExtension,
                instance.EnabledExtensions,
                StringComparer.Ordinal);

            _output.WriteLine(
                $"Device: queueFamily={device.QueueFamilyIndex}, "
                    + $"maxImageDimension2D={device.Limits.MaxImageDimension2D}, "
                    + $"maxImageArrayLayers={device.Limits.MaxImageArrayLayers}, "
                    + $"maxColorAttachments={device.Limits.MaxColorAttachments}, "
                    + $"maxFramebuffer={device.Limits.MaxFramebufferWidth}x"
                    + $"{device.Limits.MaxFramebufferHeight}, "
                    + "maxPerStageDescriptorUpdateAfterBindSampledImages="
                    + $"{device.Limits.MaxPerStageDescriptorUpdateAfterBindSampledImages}");
        }
        finally
        {
            device?.Destroy();
            instance?.Destroy();
        }

        Assert.NotNull(instance);
        IVulkanInstance createdInstance = instance!;
        Assert.Null(createdInstance.FatalException);
        Assert.Equal(0, createdInstance.ValidationErrorCount);
        Assert.Equal(0, createdInstance.ValidationWarningCount);

        Assert.Contains(
            provider.Entries,
            entry => entry.Message.Contains("Vulkan instance created", StringComparison.Ordinal));
        Assert.Contains(
            provider.Entries,
            entry => entry.Message.Contains("Vulkan device selected", StringComparison.Ordinal));
        Assert.DoesNotContain(
            provider.Entries,
            entry => entry.Message.Contains(
                "Vulkan object naming returned",
                StringComparison.Ordinal));
    }

    private static bool IsUnavailable(Bork2dException exception)
        => exception.Message.Contains("could not be loaded", StringComparison.Ordinal)
            || exception.Message.Contains("No Vulkan physical devices", StringComparison.Ordinal)
            || exception.Message.Contains("No suitable Vulkan 1.3 device", StringComparison.Ordinal)
            || exception.Message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || exception.Message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || exception.Message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || exception.Message.Contains(
                "maxPerStageDescriptorUpdateAfterBindSampledImages=0",
                StringComparison.Ordinal);
}
