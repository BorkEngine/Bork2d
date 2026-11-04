using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof that the color pipeline is linear end to end: the 2D passes render linear
/// color into the scene image and the final present pass applies the sRGB opto-electronic transfer
/// function into the UNORM target.
/// </summary>
/// <remarks>
/// The tests clear a camera to a known linear color, capture the final target, and assert the encoded
/// byte. They skip when the Vulkan loader/device/validation layer is unavailable (D-M6-05, D-M6-20).
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class PresentGammaGpuTests
{
    private const int FramebufferSize = 16;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    [GpuFact]
    public async Task ALinearHalfClearIsEncodedByThePresentPass()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            var clearColor = new Color(0.5f, 0.5f, 0.5f, 1f);

            FrameCaptureResult result = await CaptureAsync(
                renderer,
                producer,
                snapshot => AddCamera(snapshot, clearColor, viewport: null));

            // 0.5 linear stores as UNORM 128 and the present pass encodes it to 188/255.
            AssertPixelNear(result, 2, 2, 188, 188, 188, 255, tolerance: 2, "the linear half clear");
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(0, stats.NonEmptyCameras);
            Assert.Equal(1, stats.DrawCalls);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task LinearBlackAndWhiteClearsEncodeToZeroAndFullBytes()
    {
        using var assets = new TestRendererAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            FrameCaptureResult result = await CaptureAsync(
                renderer,
                producer,
                snapshot =>
                {
                    AddCamera(
                        snapshot,
                        new Color(0f, 0f, 0f, 1f),
                        new Vector4(0f, 0f, 0.5f, 1f));
                    AddCamera(
                        snapshot,
                        new Color(1f, 1f, 1f, 1f),
                        new Vector4(0.5f, 0f, 0.5f, 1f));
                });

            AssertPixelNear(result, 2, 2, 0, 0, 0, 255, tolerance: 1, "the linear black clear");
            AssertPixelNear(
                result,
                FramebufferSize - 2,
                2,
                255,
                255,
                255,
                255,
                tolerance: 1,
                "the linear white clear");
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(2, stats.Cameras);
            Assert.Equal(0, stats.NonEmptyCameras);
            Assert.Equal(1, stats.DrawCalls);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new CapturingLoggerProvider());
        });

    private static VulkanOffscreenRenderer StartOffscreen(
        TestRendererAssets assets,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = FramebufferSize,
            Height = FramebufferSize,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The present-gamma offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException(
                "The present-gamma renderer faulted during startup.",
                fatal);
        }

        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        return renderer;
    }

    private static async Task<FrameCaptureResult> CaptureAsync(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
        long sequence = renderer.RenderStats.FrameSequence;
        producer.Publish(fill);
        if (!renderer.WaitForProgress(
            () => renderer.RenderStats.FrameSequence > sequence,
            _timeout))
        {
            throw new TimeoutException(
                $"The capture frame was not published within {_timeout.TotalSeconds:0} s. "
                    + Describe(renderer.RenderStats));
        }

        return await capture.WaitAsync(_timeout);
    }

    private static void AddCamera(FrameSnapshot snapshot, Color clearColor, Vector4? viewport)
        => snapshot.AddCamera(
            new CameraSnapshot(
                ZIndex: 0,
                WorldMatrix: Matrix3x2.Identity,
                Size: 10f,
                Viewport: viewport ?? new Vector4(0f, 0f, 1f, 1f),
                CullingMask: 1u,
                Clear: ClearKind.Color,
                ClearColor: clearColor));

    private static void StopAndAssertClean(VulkanOffscreenRenderer renderer)
    {
        renderer.RequestStop();
        Assert.True(renderer.Join(_joinTimeout), "The renderer did not join.");
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);
    }

    private static void Finish(VulkanOffscreenRenderer? renderer)
    {
        if (renderer is not null)
        {
            renderer.RequestStop();
            _ = renderer.Join(_joinTimeout);
        }
    }

    private static string Describe(RenderStats stats)
        => $"frame={stats.FrameSequence}, cameras={stats.Cameras}, "
            + $"draws={stats.DrawCalls}, validation={stats.ValidationErrors}/"
            + $"{stats.ValidationWarnings}";

    private static void AssertPixelNear(
        FrameCaptureResult result,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a,
        int tolerance,
        string because)
    {
        int offset = ((y * result.Width) + x) * 4;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        byte actualR = pixels[offset];
        byte actualG = pixels[offset + 1];
        byte actualB = pixels[offset + 2];
        byte actualA = pixels[offset + 3];
        Assert.True(
            Math.Abs(actualR - r) <= tolerance
                && Math.Abs(actualG - g) <= tolerance
                && Math.Abs(actualB - b) <= tolerance
                && Math.Abs(actualA - a) <= tolerance,
            $"{because}: pixel ({x}, {y}) is ({actualR}, {actualG}, {actualB}, {actualA}), "
                + $"expected ({r}, {g}, {b}, {a}) +/- {tolerance}.");
    }

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
            || message.Contains("No UNORM offscreen color format", StringComparison.Ordinal);
    }

    private sealed class Producer
    {
        private readonly VulkanOffscreenRenderer _renderer;
        private long _published;

        internal Producer(VulkanOffscreenRenderer renderer) => _renderer = renderer;

        internal void Publish(Action<FrameSnapshot> fill)
        {
            var slot = _renderer.AcquireSnapshotSlot()
                ?? throw new InvalidOperationException(
                    "The renderer stopped consuming; the test producer cannot publish.");
            slot.Snapshot.Reset();
            slot.Snapshot.Sequence = ++_published;
            slot.Snapshot.Time = _published * 0.016;
            slot.Snapshot.DeltaTime = 0.016;
            fill(slot.Snapshot);
            _renderer.PublishSnapshot(slot);
        }
    }
}
