using System.Runtime.InteropServices;
using Bork2d.Render;
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
/// Opt-in real-GPU proof for the offscreen renderer: a fixed 320x240 sRGB target, 30+ discarded
/// frames, no surface/swapchain, a bounded stop, zero validation errors/warnings, and clean
/// shutdown.
/// </summary>
/// <remarks>
/// The test skips when the Vulkan loader, a suitable device, or the validation layer is unavailable
/// (D-M6-05, D-M6-20). There is no readback (D-M6-23) and no sleep or spin: frame progress is
/// observed through the renderer's event-based wait.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class OffscreenRendererGpuTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="OffscreenRendererGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public OffscreenRendererGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void OffscreenRendererClearsDiscardsAndStopsCleanlyWithoutSurfaceOrSwapchain()
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

        VulkanOffscreenRenderer? renderer = null;
        try
        {
            var startInfo = new RendererStartInfo
            {
                Mode = RendererMode.Offscreen,
                InstanceHandle = 0,
                Width = 320,
                Height = 240,
                ValidationRequested = true,
                LoggerFactory = loggerFactory,
            };

            renderer = Assert.IsType<VulkanOffscreenRenderer>(
                new VulkanRendererFactory().Create(startInfo));
            renderer.Start();

            if (!renderer.WaitForReady(_timeout))
            {
                throw new TimeoutException(
                    "The offscreen renderer did not become ready within the bounded wait.");
            }

            if (renderer.FatalException is { } startupFatal)
            {
                if (IsUnavailable(startupFatal))
                {
                    throw SkipException.ForSkip(
                        $"No suitable Vulkan 1.3 offscreen device is available: "
                            + startupFatal.Message);
                }

                throw new InvalidOperationException(
                    "The offscreen renderer faulted during startup.",
                    startupFatal);
            }

            if (!renderer.ValidationEnabled)
            {
                throw SkipException.ForSkip(
                    $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot "
                        + "be gathered on this machine.");
            }

            Assert.Equal(RendererStatus.Ready, renderer.Status);
            Assert.Null(startInfo.Instance);
            Assert.Equal(0, startInfo.SurfaceHandle);
            _output.WriteLine($"ready: {Describe(renderer.Counters)}");

            if (!renderer.WaitForProgress(
                counters => counters.FramesDiscarded >= 30,
                _timeout))
            {
                throw new TimeoutException(
                    "The offscreen renderer discarded fewer than 30 frames within the bounded wait. "
                        + Describe(renderer.Counters));
            }

            _output.WriteLine($"after 30+ discarded frames: {Describe(renderer.Counters)}");
            long framesAtStop = renderer.Counters.FramesDiscarded;

            renderer.RequestStop();
            Assert.True(
                renderer.Join(_joinTimeout),
                $"The offscreen renderer did not join within {_joinTimeout.TotalSeconds:0} s.");

            RendererCounters counters = renderer.Counters;
            Assert.Equal(RendererStatus.Stopped, renderer.Status);
            Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
            Assert.Null(renderer.FatalException);
            Assert.Equal(0, counters.FramesPresented);
            Assert.True(
                counters.FramesDiscarded >= 30,
                $"Only {counters.FramesDiscarded} offscreen frames were discarded.");
            Assert.True(
                counters.FramesDiscarded >= framesAtStop,
                "Stop must not reduce the discarded-frame counter.");
            Assert.Equal(0, counters.SwapchainRecreations);
            Assert.Equal(0, counters.ValidationErrorCount);
            Assert.Equal(0, counters.ValidationWarningCount);
            Assert.Equal(0, counters.LastPresentResult);

            _output.WriteLine($"final counters: {Describe(counters)}");
            _output.WriteLine(
                "surface=0 (offscreen), swapchain=0, readback=none (D-M6-23).");
        }
        finally
        {
            if (renderer is not null)
            {
                renderer.RequestStop();
                _ = renderer.Join(_joinTimeout);
                _output.WriteLine(
                    $"pre-cleanup: status={renderer.Status}, stopReason={renderer.StopReason}, "
                        + $"fatal={renderer.FatalException?.Message ?? "(none)"}, "
                        + $"counters={Describe(renderer.Counters)}");
            }

            WriteRendererLog(provider);
        }
    }

    private void WriteRendererLog(CapturingLoggerProvider provider)
    {
        foreach (CapturingLoggerProvider.CapturedLog entry in provider.Entries)
        {
            if (entry.Category == RenderLog.Category && entry.Level >= LogLevel.Information)
            {
                _output.WriteLine($"{entry.Level}: {entry.Message}");
            }
        }
    }

    private static string Describe(RendererCounters counters)
        => $"framesPresented={counters.FramesPresented}, "
            + $"framesDiscarded={counters.FramesDiscarded}, "
            + $"swapchainRecreations={counters.SwapchainRecreations}, "
            + $"validationErrors={counters.ValidationErrorCount}, "
            + $"validationWarnings={counters.ValidationWarningCount}, "
            + $"lastFrameNumber={counters.LastFrameNumber}, "
            + $"lastPresentResult={counters.LastPresentResult}";

    private static bool IsUnavailable(Exception exception)
    {
        string message = exception.Message;
        return message.Contains("could not be loaded", StringComparison.Ordinal)
            || message.Contains("No Vulkan physical devices", StringComparison.Ordinal)
            || message.Contains("No suitable Vulkan 1.3 device", StringComparison.Ordinal)
            || message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || message.Contains(
                "maxPerStageDescriptorUpdateAfterBindSampledImages=0",
                StringComparison.Ordinal)
            || message.Contains("No sRGB offscreen color format", StringComparison.Ordinal);
    }
}
