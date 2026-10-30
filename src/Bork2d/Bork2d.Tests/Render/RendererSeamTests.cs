using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
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
        instance.DestroySurface(0x40);
        instance.Destroy();
        instance.Destroy();

        Assert.Equal((ObjectType.Device, (nint)0x20, "device"), instance.ObjectNames[0]);
        Assert.Equal(2, instance.CommandBufferLabels.Count);
        Assert.Equal((nint)0x30, instance.CommandBufferLabels[0].CommandBuffer);
        Assert.Null(instance.CommandBufferLabels[1].Label);
        Assert.Equal((nint)0x40, Assert.Single(instance.DestroyedSurfaces));
        Assert.Equal(1, instance.DestroyCount);
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
