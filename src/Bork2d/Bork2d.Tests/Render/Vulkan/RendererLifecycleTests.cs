using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic characterization tests for the shared renderer lifecycle coordinator: start-once
/// and join semantics, readiness, status and stop reason, the one-shot fatal, counter aggregation,
/// snapshot/control delegations, progress waits, and the capture stop/fatal close path.
/// </summary>
/// <remarks>
/// The tests never load Vulkan or SDL. They drive the real coordinator with a fake frame set, so the
/// windowed and offscreen renderers do not need a GPU to prove the extracted lifecycle behavior.
/// </remarks>
public sealed class RendererLifecycleTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ReadinessIsNotReportedBeforeMarkReady()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);

        Assert.False(lifecycle.IsReady);
        Assert.False(lifecycle.WaitForReady(TimeSpan.Zero));
        Assert.Equal(RendererStatus.NotStarted, lifecycle.Status);
        Assert.Equal(RendererStopReason.None, lifecycle.StopReason);
        Assert.Null(lifecycle.FatalException);
        Assert.Equal(RendererCounters.Empty, lifecycle.Counters);
    }

    [Fact]
    public void StartIsSingleUseAndJoinIsIdempotent()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);
        lifecycle.Start(
            startup: () =>
            {
                lifecycle.MarkReady();
                return true;
            },
            frames: () => WaitForStop(lifecycle),
            cleanup: static () => { },
            publishFatal: lifecycle.PublishFatal,
            logStopped: static () => { });

        Assert.True(lifecycle.WaitForReady(_timeout));
        Assert.Equal(RendererStatus.Ready, lifecycle.Status);
        Assert.Throws<InvalidOperationException>(
            () => lifecycle.Start(
                startup: static () => true,
                frames: static () => { },
                cleanup: static () => { },
                publishFatal: lifecycle.PublishFatal,
                logStopped: static () => { }));

        lifecycle.RequestStop();
        Assert.True(lifecycle.Join(_timeout));
        Assert.True(lifecycle.Join(_timeout));
        Assert.Equal(RendererStatus.Stopped, lifecycle.Status);
        Assert.Equal(RendererStopReason.Requested, lifecycle.StopReason);
        Assert.Null(lifecycle.FatalException);
    }

    [Fact]
    public void RequestStopMovesAStartingRendererToStopping()
    {
        using var unblock = new ManualResetEventSlim(initialState: false);
        RendererLifecycle lifecycle = CreateLifecycle(out _);
        lifecycle.Start(
            startup: static () => true,
            frames: () => unblock.Wait(),
            cleanup: static () => { },
            publishFatal: lifecycle.PublishFatal,
            logStopped: static () => { });

        Assert.Equal(RendererStatus.Starting, lifecycle.Status);

        lifecycle.RequestStop();
        Assert.Equal(RendererStatus.Stopping, lifecycle.Status);
        Assert.Equal(RendererStopReason.Requested, lifecycle.StopReason);

        unblock.Set();
        Assert.True(lifecycle.Join(_timeout));
        Assert.Equal(RendererStatus.Stopped, lifecycle.Status);
    }

    [Fact]
    public void StartupCanSkipTheFrameLoopAfterAStopRequest()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);
        var framesRan = false;
        lifecycle.Start(
            startup: static () => false,
            frames: () => framesRan = true,
            cleanup: static () => { },
            publishFatal: lifecycle.PublishFatal,
            logStopped: static () => { });

        Assert.True(lifecycle.Join(_timeout));
        Assert.False(framesRan);
        Assert.Equal(RendererStatus.Stopped, lifecycle.Status);
        Assert.Equal(RendererStopReason.Requested, lifecycle.StopReason);
    }

    [Fact]
    public void FatalRunFaultsPendingCapturesAndReportsTheFaultedState()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out RendererCaptureService captures);
        var fatal = new Bork2dException("boom");
        Task<FrameCaptureResult> pending = captures.Request(default);
        lifecycle.Start(
            startup: () =>
            {
                lifecycle.MarkReady();
                return true;
            },
            frames: () => throw fatal,
            cleanup: static () => { },
            publishFatal: lifecycle.PublishFatal,
            logStopped: static () => { });

        Assert.True(lifecycle.Join(_timeout));
        Assert.Equal(RendererStatus.Faulted, lifecycle.Status);
        Assert.Equal(RendererStopReason.Fatal, lifecycle.StopReason);
        Assert.Same(fatal, lifecycle.FatalException);
        Assert.True(pending.IsFaulted);
        Assert.Same(fatal, pending.Exception!.InnerExceptions[0]);
    }

    [Fact]
    public void PublishFatalStopsTheRingAndFaultsPendingCaptures()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out RendererCaptureService captures);
        Task<FrameCaptureResult> pending = captures.Request(default);
        var fatal = new Bork2dException("boom");

        lifecycle.PublishFatal(fatal);

        Assert.Equal(RendererStatus.Faulted, lifecycle.Status);
        Assert.Equal(RendererStopReason.Fatal, lifecycle.StopReason);
        Assert.Same(fatal, lifecycle.FatalException);
        Assert.True(lifecycle.IsStopping);
        Assert.True(pending.IsFaulted);
        Assert.Same(fatal, pending.Exception!.InnerExceptions[0]);
    }

    [Theory]
    [InlineData(nameof(VulkanRenderer), "windowed")]
    [InlineData(nameof(VulkanOffscreenRenderer), "offscreen")]
    public void StopFaultsPendingCapturesWithTheRendererSpecificFailure(
        string ownerName,
        string modeDescription)
    {
        RendererLifecycle lifecycle = CreateLifecycle(out RendererCaptureService captures, ownerName, modeDescription);
        Task<FrameCaptureResult> pending = captures.Request(default);

        lifecycle.RequestStop();

        Assert.Equal(RendererStopReason.Requested, lifecycle.StopReason);
        Assert.True(pending.IsFaulted);
        var failure = Assert.IsType<ObjectDisposedException>(pending.Exception!.InnerExceptions[0]);
        Assert.Equal(ownerName, failure.ObjectName);
        Assert.Contains($"{modeDescription} renderer stopped", failure.Message, StringComparison.Ordinal);
        Assert.True(lifecycle.Join(TimeSpan.Zero));

        Task<FrameCaptureResult> rejected = captures.Request(default);
        Assert.True(rejected.IsFaulted);
        var closed = Assert.IsType<ObjectDisposedException>(rejected.Exception!.InnerExceptions[0]);
        Assert.Equal(nameof(CaptureRequestQueue), closed.ObjectName);
    }

    [Fact]
    public void CountersAggregateTheSharedCounterSlots()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);

        lifecycle.IncrementFramesPresented();
        lifecycle.IncrementFramesDiscarded();
        lifecycle.IncrementSwapchainRecreations();
        lifecycle.IncrementLastFrameNumber();
        lifecycle.SetLastPresentResult(-9);

        RendererCounters counters = lifecycle.Counters;
        Assert.Equal(1, counters.FramesPresented);
        Assert.Equal(1, counters.FramesDiscarded);
        Assert.Equal(1, counters.SwapchainRecreations);
        Assert.Equal(1, counters.LastFrameNumber);
        Assert.Equal(-9, counters.LastPresentResult);
    }

    [Fact]
    public void SnapshotDelegationsUseTheSharedRing()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);

        SnapshotSlot slot = Assert.IsType<SnapshotSlot>(lifecycle.AcquireSnapshotSlot());
        lifecycle.PublishSnapshot(slot);

        Assert.Equal(1, lifecycle.SnapshotCounters.Published);
        Assert.True(lifecycle.Consumer.HasReadySnapshot);
        Assert.True(lifecycle.Join(TimeSpan.Zero));
    }

    [Fact]
    public void WaitForProgressObservesSatisfiedAndUnsatisfiedPredicates()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);

        Assert.True(lifecycle.WaitForProgress(static () => true, TimeSpan.Zero));
        Assert.False(lifecycle.WaitForProgress(static () => false, TimeSpan.Zero));
    }

    [Fact]
    public void WaitForProgressWakesWhenProgressIsSignaled()
    {
        RendererLifecycle lifecycle = CreateLifecycle(out _);
        var worker = new Thread(() =>
        {
            lifecycle.IncrementFramesDiscarded();
            lifecycle.SignalProgress();
        })
        {
            IsBackground = true,
        };

        worker.Start();
        Assert.True(
            lifecycle.WaitForProgress(
                () => lifecycle.Counters.FramesDiscarded > 0,
                _timeout));
        worker.Join();
    }

    private static void WaitForStop(RendererLifecycle lifecycle)
    {
        while (lifecycle.Wait.Wait() != SnapshotWaitOutcome.Stopped)
        {
        }
    }

    private static RendererLifecycle CreateLifecycle(
        out RendererCaptureService captures,
        string ownerName = nameof(VulkanRenderer),
        string modeDescription = "windowed")
    {
        var frames = new FakeVulkanFrameSet();
        captures = new RendererCaptureService(frames, ownerName, modeDescription);
        return new RendererLifecycle(NullLogger.Instance, captures, 2);
    }

    /// <summary>A frame set with no GPU frames; the lifecycle tests never touch Vulkan.</summary>
    private sealed class FakeVulkanFrameSet : IVulkanFrameSet
    {
        public int Count => 0;

        public bool HasInstanceResources => false;

        public int InstanceCapacityHighWater => 0;

        public int InstanceGrowthCount => 0;

        public int StopLogInstanceCapacity => 0;

        public int StopLogInstanceGrowths => 0;

        public IVulkanFrame? GetFrame(int index) => null;

        public void CaptureStopCounters()
        {
        }

        public void EnsureBindlessCapacity(VulkanDevice device, int capacity)
            => throw new NotSupportedException("The lifecycle tests never grow descriptor sets.");

        public void WritePage(int pageIndex, ImageView imageView, Sampler sampler)
            => throw new NotSupportedException("The lifecycle tests never write page descriptors.");

        public void WaitForFences(VulkanDevice device)
            => throw new NotSupportedException("The lifecycle tests never wait GPU fences.");

        public void DisposeFrames()
        {
        }
    }
}
