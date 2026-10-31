using System.Runtime.InteropServices;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the offscreen renderer's M7 handoff: a fixed 320x240 sRGB target, a
/// test producer publishing snapshots one at a time, exactly one discarded frame per consumed
/// snapshot, a stop with a frame in flight, zero validation errors/warnings, every slot Free after
/// Join, and clean shutdown.
/// </summary>
/// <remarks>
/// The test skips when the Vulkan loader, a suitable device, or the validation layer is unavailable
/// (D-M6-05, D-M6-20). There is no readback (D-M6-23) and no sleep or spin: progress is observed
/// through the renderer's event-based wait, and frame counts are asserted as relationships, not
/// equality with wall-clock time (Q19=S3).
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
    public void OffscreenRendererConsumesOneSnapshotPerDiscardedFrameAndStopsCleanly()
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
            Assert.True(
                renderer.HasInstanceResources,
                "The renderer did not create the per-frame instance SSBO/descriptor set (Q10=J1).");
            _output.WriteLine($"ready: {Describe(renderer.Counters)}");

            var producer = new TestSnapshotProducer(renderer);
            const int snapshots = 8;
            for (var index = 0; index < snapshots; index++)
            {
                producer.PublishOne();
                WaitFor(
                    renderer,
                    () => renderer.Counters.FramesDiscarded >= producer.Published,
                    "A published snapshot did not discard its frame",
                    _timeout);
            }

            Assert.Equal((long)snapshots, renderer.SnapshotCounters.Published);
            Assert.Equal((long)snapshots, renderer.SnapshotCounters.Consumed);
            Assert.Equal(renderer.SnapshotCounters.Consumed, renderer.Counters.FramesDiscarded);
            Assert.True(
                renderer.Counters.FramesDiscarded >= snapshots,
                $"Only {renderer.Counters.FramesDiscarded} offscreen frames were discarded.");
            Assert.Equal(0, renderer.Counters.FramesPresented);
            _output.WriteLine(
                $"after {snapshots} snapshots: {Describe(renderer.Counters)}; "
                    + $"ring={DescribeRing(renderer)}");

            // Stop with a frame in flight: publish and stop immediately, then join and drain.
            producer.PublishOne();
            long discardedAtStop = renderer.Counters.FramesDiscarded;
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
                counters.FramesDiscarded >= discardedAtStop,
                "Stop must not reduce the discarded-frame counter.");
            Assert.Equal(renderer.SnapshotCounters.Consumed, counters.FramesDiscarded);
            Assert.True(
                renderer.SnapshotCounters.Consumed <= renderer.SnapshotCounters.Published,
                "Consumed snapshots must never exceed published snapshots.");
            Assert.True(
                renderer.SnapshotCounters.Consumed >= snapshots,
                "Every published snapshot before the stop must have been consumed.");
            Assert.Equal(0, counters.SwapchainRecreations);
            Assert.Equal(0, counters.ValidationErrorCount);
            Assert.Equal(0, counters.ValidationWarningCount);
            Assert.Equal(0, counters.LastPresentResult);

            // Join is the quiescent point: every slot is Free.
            foreach (var slot in renderer.SnapshotRing.Slots)
            {
                Assert.Equal(SnapshotSlotState.Free, slot.State);
            }

            Assert.Equal(-1, renderer.SnapshotRing.LatestIndex);

            _output.WriteLine($"final counters: {Describe(counters)}");
            _output.WriteLine(
                $"final ring: published={renderer.SnapshotCounters.Published}, "
                    + $"consumed={renderer.SnapshotCounters.Consumed}, "
                    + $"superseded={renderer.SnapshotCounters.Superseded}, "
                    + $"backpressureWaits={renderer.SnapshotCounters.BackpressureWaits}, "
                    + $"maxLive={renderer.SnapshotCounters.MaxLive}, "
                    + $"suspended={renderer.SuspendedConsumptions}");
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

    private void WaitFor(
        VulkanOffscreenRenderer renderer,
        Func<bool> predicate,
        string message,
        TimeSpan timeout)
    {
        if (renderer.WaitForProgress(predicate, timeout))
        {
            return;
        }

        if (renderer.FatalException is { } fatal)
        {
            throw new InvalidOperationException(
                $"{message}. Renderer fatal: {fatal.Message}",
                fatal);
        }

        throw new TimeoutException(
            $"{message} within {timeout.TotalSeconds:0} s. {Describe(renderer.Counters)}; "
                + $"ring={DescribeRing(renderer)}.");
    }

    private static string DescribeRing(VulkanOffscreenRenderer renderer)
        => $"slots=[{string.Join(
            ", ",
            renderer.SnapshotRing.Slots.ToArray().Select(slot => slot.State.ToString()))}], "
            + $"latest={renderer.SnapshotRing.LatestIndex}";

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

    /// <summary>
    /// The test producer: fills one slot through the renderer's internal slot API and publishes it.
    /// </summary>
    private sealed class TestSnapshotProducer
    {
        private readonly VulkanOffscreenRenderer _renderer;
        private long _published;

        internal TestSnapshotProducer(VulkanOffscreenRenderer renderer) => _renderer = renderer;

        internal long Published => _published;

        internal void PublishOne()
        {
            SnapshotSlot? slot = _renderer.AcquireSnapshotSlot();
            if (slot is null)
            {
                throw new InvalidOperationException(
                    "The renderer stopped consuming; the test producer cannot publish.");
            }

            slot.Snapshot.Reset();
            slot.Snapshot.Sequence = ++_published;
            slot.Snapshot.Time = _published * 0.016;
            slot.Snapshot.DeltaTime = 0.016;
            _renderer.PublishSnapshot(slot);
        }
    }
}
