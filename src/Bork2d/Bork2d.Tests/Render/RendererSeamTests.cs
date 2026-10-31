using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// Deterministic tests for the internal renderer seam and its fake; no Vulkan call is involved.
/// </summary>
public sealed class RendererSeamTests
{
    [Fact]
    public void FactoryReceivesTheOpaqueHandlesWithoutStartingTheRenderer()
    {
        var factory = new FakeRendererFactory();
        RendererStartInfo startInfo = StartInfo(
            instanceHandle: 0x1234,
            surfaceHandle: 0x5678);

        IRenderer renderer = factory.Create(startInfo);

        Assert.Same(factory.Renderer, renderer);
        Assert.Equal(1, factory.CreateCount);
        Assert.Same(startInfo, factory.StartInfo);
        Assert.Equal(0x1234, factory.StartInfo!.InstanceHandle);
        Assert.Equal(0x5678, factory.StartInfo.SurfaceHandle);
        Assert.False(factory.Renderer.Started);
    }

    [Fact]
    public void StartReturnsBeforeReadiness()
    {
        var renderer = new FakeRenderer();

        renderer.Start();

        Assert.True(renderer.Started);
        Assert.Equal(RendererStatus.Starting, renderer.Status);
        Assert.False(renderer.IsReady);
        Assert.False(renderer.WaitForReady(TimeSpan.Zero));

        renderer.SetReady();

        Assert.True(renderer.IsReady);
        Assert.Equal(RendererStatus.Ready, renderer.Status);
        Assert.True(renderer.WaitForReady(TimeSpan.Zero));
    }

    [Fact]
    public void ReadinessFailureSignalsWaitersAndJoins()
    {
        var renderer = new FakeRenderer();
        var failure = new Bork2dException("no suitable device");
        renderer.Start();

        renderer.FailReadiness(failure);

        Assert.True(renderer.WaitForReady(TimeSpan.Zero));
        Assert.Same(failure, renderer.FatalException);
        Assert.Equal(RendererStatus.Faulted, renderer.Status);
        Assert.Equal(RendererStopReason.Fatal, renderer.StopReason);
        Assert.True(renderer.Join(TimeSpan.Zero));
        Assert.True(renderer.Joined);
    }

    [Fact]
    public void PostStartFatalStopsTheFakeAndJoins()
    {
        var renderer = new FakeRenderer();
        var failure = new Bork2dException("device lost");
        renderer.Start();
        renderer.SetReady();

        renderer.PublishFatal(failure);

        Assert.Same(failure, renderer.FatalException);
        Assert.Equal(RendererStatus.Faulted, renderer.Status);
        Assert.Equal(RendererStopReason.Fatal, renderer.StopReason);
        Assert.True(renderer.Join(TimeSpan.Zero));
    }

    [Fact]
    public void ControlPostsKeepTheLatestValue()
    {
        var renderer = new FakeRenderer();

        renderer.PostWindowExtent(new WindowExtent(800, 600));
        renderer.PostWindowExtent(new WindowExtent(1600, 900));
        renderer.PostFullscreen(true);
        renderer.PostFullscreen(false);
        renderer.PostMinimized(true);

        Assert.Equal(2, renderer.ExtentPostCount);
        Assert.Equal(2, renderer.FullscreenPostCount);
        Assert.Equal(1, renderer.MinimizedPostCount);
        Assert.Equal(new WindowExtent(1600, 900), renderer.LastExtent);
        Assert.False(renderer.LastFullscreen);
        Assert.True(renderer.LastMinimized);
    }

    [Fact]
    public void RequestStopAndJoinCompletesTheFake()
    {
        var renderer = new FakeRenderer();
        renderer.Start();
        renderer.SetReady();

        renderer.RequestStop();

        Assert.True(renderer.StopRequested);
        Assert.True(renderer.Join(TimeSpan.Zero));
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
    }

    [Fact]
    public void CountersAreObservableThroughTheSeam()
    {
        var renderer = new FakeRenderer
        {
            Counters = new RendererCounters(
                FramesPresented: 12,
                FramesDiscarded: 0,
                SwapchainRecreations: 2,
                ValidationErrorCount: 0,
                ValidationWarningCount: 0,
                LastFrameNumber: 11,
                LastPresentResult: 0),
        };

        IRenderer seam = renderer;

        Assert.Equal(12, seam.Counters.FramesPresented);
        Assert.Equal(2, seam.Counters.SwapchainRecreations);
        Assert.Equal(RendererCounters.Empty, default(RendererCounters));
    }

    [Fact]
    public void FakeVulkanInstanceRecordsDebugLabelsAndSurfaceDestruction()
    {
        var instance = new FakeVulkanInstance
        {
            ValidationEnabled = true,
            DebugUtilsEnabled = true,
            EnabledLayers = ["VK_LAYER_KHRONOS_validation"],
            EnabledExtensions = ["VK_EXT_debug_utils"],
        };

        instance.SetObjectName(0x10, ObjectType.Device, 0x20, "device");
        instance.BeginCommandBufferLabel(0x10, 0x30, "clear");
        instance.EndCommandBufferLabel(0x10, 0x30);
        nint cachedLabel = instance.CreateDebugLabel("Bork2d clear pass");
        instance.BeginCachedCommandBufferLabel(0x10, 0x30, cachedLabel);
        instance.DestroyDebugLabel(cachedLabel);
        instance.DestroySurface(0x40);
        instance.Destroy();
        instance.Destroy();

        Assert.Equal((ObjectType.Device, (nint)0x20, "device"), instance.ObjectNames[0]);
        Assert.Equal(2, instance.CommandBufferLabels.Count);
        Assert.Equal((nint)0x30, instance.CommandBufferLabels[0].CommandBuffer);
        Assert.Null(instance.CommandBufferLabels[1].Label);
        Assert.NotEqual(0, cachedLabel);
        Assert.Equal(1, instance.CreatedDebugLabelCount);
        Assert.Equal(1, instance.DestroyedDebugLabelCount);
        Assert.Equal(
            ((nint)0x30, cachedLabel),
            Assert.Single(instance.CachedCommandBufferLabels));
        Assert.Equal((nint)0x40, Assert.Single(instance.DestroyedSurfaces));
        Assert.Equal(1, instance.DestroyCount);
    }

    [Fact]
    public void FakeRendererDrivesTheSnapshotSlotHandoff()
    {
        var renderer = new FakeRenderer();
        renderer.Start();
        renderer.SetReady();

        SnapshotSlot? slot = renderer.AcquireSnapshotSlot();
        Assert.NotNull(slot);
        slot!.Snapshot.Reset();
        slot.Snapshot.Sequence = 7;
        renderer.PublishSnapshot(slot);

        // AutoConsume models a renderer that keeps up: the published snapshot is consumed and the
        // slot is free again, so the producer never blocks.
        Assert.Equal(1, renderer.SnapshotCounters.Published);
        Assert.Equal(1, renderer.SnapshotCounters.Consumed);
        Assert.Equal(SnapshotSlotState.Free, slot.State);
        Assert.Equal(-1, renderer.SnapshotRing.LatestIndex);
        Assert.NotNull(renderer.AcquireSnapshotSlot());
    }

    [Fact]
    public void FakeRendererHoldModeExposesBackpressureUntilStop()
    {
        var renderer = new FakeRenderer { AutoConsume = false };
        renderer.Start();
        renderer.SetReady();

        for (var index = 0; index < SnapshotRing.SlotCapacity; index++)
        {
            SnapshotSlot slot = Assert.IsType<SnapshotSlot>(renderer.AcquireSnapshotSlot());
            renderer.PublishSnapshot(slot);
        }

        Assert.Equal(SnapshotRing.SlotCapacity, renderer.SnapshotCounters.Published);
        Assert.Equal(2, renderer.HeldSnapshotCount);
        Assert.Equal(2, renderer.SnapshotCounters.Consumed);
        Assert.True(renderer.SnapshotRing.HasReadySnapshot);

        using var started = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var result = new SnapshotSlot?[1];
        var producer = new Thread(() =>
        {
            started.Set();
            result[0] = renderer.AcquireSnapshotSlot();
            completed.Set();
        })
        {
            IsBackground = true,
        };

        producer.Start();
        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        Assert.False(completed.Wait(TimeSpan.FromMilliseconds(250)));

        renderer.RequestStop();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(producer.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(result[0]);
        Assert.True(renderer.SnapshotRing.IsStopped);

        // Join is the quiescent point: held and pending slots are released.
        Assert.True(renderer.Join(TimeSpan.FromSeconds(30)));
        foreach (var slot in renderer.SnapshotRing.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }
    }

    [Fact]
    public void FakeRendererWakeExitAndAbandonKeepTheRingConsistent()
    {
        var renderer = new FakeRenderer { AutoConsume = false };
        renderer.Start();
        renderer.SetReady();

        SnapshotSlot slot = Assert.IsType<SnapshotSlot>(renderer.AcquireSnapshotSlot());
        renderer.AbandonSnapshotSlot(slot);
        Assert.Equal(SnapshotSlotState.Free, slot.State);

        renderer.WakeSnapshotWait();
        Assert.True(renderer.SnapshotRing.IsExitSignalled);
        Assert.Null(renderer.AcquireSnapshotSlot());
        Assert.True(renderer.SnapshotCounters.ExitWakes >= 1);
    }

    [Fact]
    public void FakeRendererSuspendedDrainCountsWithoutConsuming()
    {
        var renderer = new FakeRenderer { AutoConsume = false };
        renderer.Start();
        renderer.SetReady();

        SnapshotSlot latest = null!;
        for (var index = 0; index < SnapshotRing.SlotCapacity; index++)
        {
            SnapshotSlot slot = Assert.IsType<SnapshotSlot>(renderer.AcquireSnapshotSlot());
            renderer.PublishSnapshot(slot);
            latest = slot;
        }

        // Two snapshots are held Rendering (frames in flight); the newest is still Ready.
        Assert.Equal(2, renderer.HeldSnapshotCount);
        Assert.Equal(2, renderer.SnapshotCounters.Consumed);

        Assert.True(renderer.DrainSuspendedSnapshot());
        Assert.Equal(SnapshotSlotState.Free, latest.State);
        Assert.Equal(1, renderer.SuspendedConsumptions);
        Assert.Equal(2, renderer.SnapshotCounters.Consumed);
        Assert.False(renderer.DrainSuspendedSnapshot());
    }

    [Fact]
    public void FakeRendererFatalStopsTheRing()
    {
        var renderer = new FakeRenderer();
        renderer.Start();
        renderer.SetReady();

        renderer.PublishFatal(new Bork2dException("device lost"));

        Assert.True(renderer.SnapshotRing.IsStopped);
        Assert.Null(renderer.AcquireSnapshotSlot());
    }

    private static RendererStartInfo StartInfo(nint instanceHandle, nint surfaceHandle)
        => new()
        {
            Mode = RendererMode.Windowed,
            InstanceHandle = instanceHandle,
            SurfaceHandle = surfaceHandle,
            Width = 1280,
            Height = 720,
            LoggerFactory = NullLoggerFactory.Instance,
        };
}
