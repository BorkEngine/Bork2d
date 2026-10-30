using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for the windowed renderer's start-info validation and thread lifecycle.
/// </summary>
/// <remarks>
/// The tests never load Vulkan or SDL: the factory rejects invalid requests synchronously, and the
/// stop-before-start path returns before device selection.
/// </remarks>
public sealed class WindowedRendererSeamTests
{
    [Fact]
    public void FactoryRejectsANullStartInfo()
    {
        var factory = new VulkanRendererFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    [Fact]
    public void FactoryRejectsWindowedWithoutAnInstance()
    {
        var factory = new VulkanRendererFactory();
        RendererStartInfo startInfo = StartInfo(instance: null, surfaceHandle: 0x1234);

        var exception = Assert.Throws<Bork2dException>(() => factory.Create(startInfo));

        Assert.Contains("instance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FactoryRejectsWindowedWithoutASurface()
    {
        var factory = new VulkanRendererFactory();
        RendererStartInfo startInfo = StartInfo(instance: new FakeVulkanInstance(), surfaceHandle: 0);

        var exception = Assert.Throws<Bork2dException>(() => factory.Create(startInfo));

        Assert.Contains("surface", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FactoryAcceptsOffscreenAndCreatesTheOffscreenRenderer()
    {
        var factory = new VulkanRendererFactory();
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = 320,
            Height = 240,
            LoggerFactory = NullLoggerFactory.Instance,
        };

        IRenderer renderer = factory.Create(startInfo);

        Assert.IsType<VulkanOffscreenRenderer>(renderer);
        Assert.Equal(RendererStatus.NotStarted, renderer.Status);
        Assert.False(renderer.IsReady);
        Assert.Equal(RendererCounters.Empty, renderer.Counters);
        Assert.True(renderer.Join(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0, 240)]
    [InlineData(320, 0)]
    [InlineData(-1, 240)]
    [InlineData(320, -1)]
    public void FactoryRejectsNonPositiveOffscreenExtents(int width, int height)
    {
        var factory = new VulkanRendererFactory();
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = width,
            Height = height,
            LoggerFactory = NullLoggerFactory.Instance,
        };

        var exception = Assert.Throws<Bork2dException>(() => factory.Create(startInfo));

        Assert.Contains("offscreen", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FactoryCreatesButDoesNotStartOrDestroyTheHostInstance()
    {
        var factory = new VulkanRendererFactory();
        var instance = new FakeVulkanInstance { DebugUtilsEnabled = true };
        RendererStartInfo startInfo = StartInfo(instance, surfaceHandle: 0x1234);

        IRenderer renderer = factory.Create(startInfo);

        Assert.IsType<VulkanRenderer>(renderer);
        Assert.Equal(RendererStatus.NotStarted, renderer.Status);
        Assert.False(renderer.IsReady);
        Assert.Equal(RendererCounters.Empty, renderer.Counters);
        Assert.Equal(0, instance.DestroyCount);
        Assert.True(renderer.Join(TimeSpan.Zero));
    }

    [Fact]
    public void StopRequestedBeforeStartJoinsWithoutTouchingVulkan()
    {
        var factory = new VulkanRendererFactory();
        var instance = new FakeVulkanInstance();
        var renderer = (VulkanRenderer)factory.Create(
            StartInfo(instance, surfaceHandle: 0x1234));

        renderer.RequestStop();
        renderer.Start();

        Assert.True(renderer.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.Equal(0, instance.DestroyCount);
    }

    [Fact]
    public void StartIsSingleUseAndJoinIsIdempotent()
    {
        var factory = new VulkanRendererFactory();
        var renderer = (VulkanRenderer)factory.Create(
            StartInfo(new FakeVulkanInstance(), surfaceHandle: 0x1234));

        renderer.RequestStop();
        renderer.Start();

        Assert.Throws<InvalidOperationException>(renderer.Start);
        Assert.True(renderer.Join(TimeSpan.FromSeconds(10)));
        Assert.True(renderer.Join(TimeSpan.FromSeconds(10)));
        Assert.True(renderer.Join(TimeSpan.FromSeconds(10)));
    }

    private static RendererStartInfo StartInfo(IVulkanInstance? instance, nint surfaceHandle)
        => new()
        {
            Mode = RendererMode.Windowed,
            Instance = instance,
            InstanceHandle = instance?.Handle ?? 0,
            SurfaceHandle = surfaceHandle,
            Width = 1280,
            Height = 720,
            VSync = true,
            ValidationRequested = true,
            LoggerFactory = NullLoggerFactory.Instance,
        };
}
