using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Textures;

/// <summary>
/// Opt-in real-GPU proof that <see cref="TextureImagePool"/> returns a retired page's range to its
/// block, coalesces adjacent free ranges, and reuses the coalesced space instead of allocating
/// another block.
/// </summary>
/// <remarks>
/// The test needs a Vulkan 1.3 loader/device and the validation layer, so it skips when either is
/// unavailable (D-M6-05, D-M6-20). It allocates raw memory ranges rather than images: the pool's
/// reuse policy is the subject, and a real image only adds creation overhead.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class TextureImagePoolGpuTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="TextureImagePoolGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public TextureImagePoolGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void FreedRangesAreReusedCoalescedAndDestroyIsIdempotent()
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
        TextureImagePool? pool = null;
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

            pool = new TextureImagePool(
                device.Api,
                device.Device,
                device.PhysicalDevice,
                loggerFactory.CreateLogger(RenderLog.Category));
            var requirements = new MemoryRequirements
            {
                Size = 4096,
                Alignment = 64,
                MemoryTypeBits = uint.MaxValue,
            };

            pool.Allocate(requirements, out DeviceMemory firstMemory, out ulong firstOffset);
            pool.Allocate(requirements, out DeviceMemory secondMemory, out ulong secondOffset);

            // Both ranges fit the one default block, laid out end to end.
            Assert.Equal(1, pool.BlockCount);
            Assert.Equal(8192UL, pool.AllocatedBytes);
            Assert.Equal(0UL, firstOffset);
            Assert.Equal(4096UL, secondOffset);

            pool.Free(secondMemory, secondOffset, requirements.Size);
            Assert.Equal(4096UL, pool.AllocatedBytes);
            pool.Free(firstMemory, firstOffset, requirements.Size);
            Assert.Equal(0UL, pool.AllocatedBytes);

            // The two adjacent ranges coalesced into the block's single free range.
            Assert.Equal(1, pool.FreeRangeCount);

            var larger = new MemoryRequirements
            {
                Size = 8192,
                Alignment = 64,
                MemoryTypeBits = uint.MaxValue,
            };
            pool.Allocate(larger, out _, out ulong largerOffset);

            // Reuse needed no second block: the coalesced range serves the larger image from the
            // block start again.
            Assert.Equal(1, pool.BlockCount);
            Assert.Equal(0UL, largerOffset);
            Assert.Equal(8192UL, pool.AllocatedBytes);

            _output.WriteLine(
                $"pool: blocks={pool.BlockCount}, allocatedBytes={pool.AllocatedBytes}, "
                    + $"freeRanges={pool.FreeRangeCount}");
        }
        finally
        {
            // Double destroy proves idempotence; the retained pool is still queryable.
            pool?.Destroy();
            pool?.Destroy();
            device?.Destroy();
            device = null;
            instance?.Destroy();
        }

        Assert.NotNull(pool);
        Assert.Equal(0, pool!.BlockCount);
        Assert.Equal(0UL, pool.AllocatedBytes);

        IVulkanInstance createdInstance = instance!;
        Assert.Equal(0, createdInstance.ValidationErrorCount);
        Assert.Equal(0, createdInstance.ValidationWarningCount);
        Assert.Contains(
            provider.Entries,
            entry => entry.Message.Contains("Vulkan device selected", StringComparison.Ordinal));
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
