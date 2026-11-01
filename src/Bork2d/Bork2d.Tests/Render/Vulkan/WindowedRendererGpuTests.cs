using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Sdl;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the windowed renderer's M7 handoff: a test producer publishes one
/// snapshot at a time, the renderer records exactly one presented frame per consumed snapshot, a
/// snapshot slot stays held until its frame fence, resize/fullscreen/minimize drain and resume, a
/// stop with a frame in flight joins cleanly, every slot ends Free, and validation is clean.
/// </summary>
/// <remarks>
/// The test thread acts as the OS/window thread. Frame/snapshot progress is observed through the
/// renderer's event-based bounded waits; there is no sleep, spin, or exact frame-equality assertion
/// (nodes: relationships only, Q19=S3). It skips when the Vulkan loader, desktop session, device,
/// or validation layer is unavailable.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class WindowedRendererGpuTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="WindowedRendererGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public WindowedRendererGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public async Task WindowedRendererConsumesOneSnapshotPerFrameAndStopsCleanly()
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

        SilkSdlApi? sdl = null;
        ISdlWindow? window = null;
        IVulkanInstance? instance = null;
        VulkanRenderer? renderer = null;
        nint surface = 0;
        var rendererJoined = true;
        try
        {
            sdl = new SilkSdlApi();
            _output.WriteLine($"SDL version: {sdl.Version}");
            _output.WriteLine($"VULKAN_SDK: {Environment.GetEnvironmentVariable("VULKAN_SDK") ?? "(unset)"}");

            try
            {
                sdl.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2);
                sdl.InitializeVideo();
            }
            catch (Bork2dException exception)
            {
                throw SkipException.ForSkip(
                    $"No SDL desktop session is available: {exception.Message}");
            }

            try
            {
                window = sdl.CreateWindow(
                    "Bork2d WP-3 windowed GPU test",
                    1280,
                    720,
                    SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi | SdlWindowFlags.Resizable);
            }
            catch (Bork2dException exception)
            {
                throw SkipException.ForSkip(
                    $"SDL could not create a Vulkan-capable window: {exception.Message}");
            }

            try
            {
                sdl.VulkanLoadLibrary();
            }
            catch (Bork2dException exception)
            {
                throw SkipException.ForSkip(
                    $"SDL could not load the Vulkan loader: {exception.Message}");
            }

            IReadOnlyList<string> extensions = window.GetVulkanInstanceExtensions();
            _output.WriteLine($"SDL Vulkan instance extensions: [{string.Join(", ", extensions)}]");

            try
            {
                instance = new VulkanInstanceFactory().Create(
                    new VulkanInstanceRequest
                    {
                        RequiredExtensions = extensions,
                        ValidationRequested = true,
                        ApplicationName = "Bork2d WP-3 windowed GPU test",
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

            surface = window.CreateVulkanSurface(instance.Handle);
            SdlWindowSize drawable = window.GetVulkanDrawableSize();
            if (drawable.IsEmpty)
            {
                throw new InvalidOperationException(
                    "SDL_Vulkan_GetDrawableSize reported a zero extent for a visible window.");
            }

            _output.WriteLine($"window={window.Size}, drawable={drawable}, validation=on");

            var assets = new GeneratedTextureAssets();
            assets.AddTexture("tex/windowed");
            var windowedSprite = new SpriteAsset
            {
                Texture = "tex/windowed",
                Uvs =
                [
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                ],
                Scale = new Vector2(8f),
                Pivot = new Vector2(0.5f, 0.5f),
                Topology = SpriteTopology.Quad,
            };

            var startInfo = new RendererStartInfo
            {
                Mode = RendererMode.Windowed,
                Instance = instance,
                InstanceHandle = instance.Handle,
                SurfaceHandle = surface,
                Width = drawable.Width,
                Height = drawable.Height,
                VSync = true,
                ValidationRequested = true,
                LoggerFactory = loggerFactory,
                Assets = assets,
                CreateReplacementSurface = () => window.CreateVulkanSurface(instance.Handle),
            };

            renderer = (VulkanRenderer)new VulkanRendererFactory().Create(startInfo);
            renderer.Start();

            if (!renderer.WaitForReady(_timeout))
            {
                throw new TimeoutException(
                    "The windowed renderer did not become ready within the bounded wait.");
            }

            if (renderer.FatalException is { } startupFatal)
            {
                if (IsUnavailable(startupFatal))
                {
                    throw SkipException.ForSkip(
                        $"No suitable Vulkan 1.3 windowed device is available: "
                            + startupFatal.Message);
                }

                throw new InvalidOperationException(
                    "The windowed renderer faulted during startup.",
                    startupFatal);
            }

            Assert.Equal(RendererStatus.Ready, renderer.Status);
            Assert.True(
                renderer.HasInstanceResources,
                "The renderer did not create the per-frame instance SSBO/descriptor set (Q10=J1).");
            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);
            Assert.True(pages.IsPlaceholderReady, "The startup placeholder page is not resident.");
            Assert.Equal(1, pages.TexturePageCount);
            Assert.Equal(TexturePageCapacity.Initial, pages.TexturePageCapacityInEntries);
            Assert.Equal(0, pages.FailedPathCount);
            _output.WriteLine(
                $"pages: resident={pages.TexturePageCount}, "
                    + $"capacity={pages.TexturePageCapacityInEntries}, "
                    + $"placeholder={pages.IsPlaceholderReady}");

            var producer = new TestSnapshotProducer(renderer, windowedSprite);
            const int initialSnapshots = 5;
            for (var index = 0; index < initialSnapshots; index++)
            {
                producer.PublishOne();
                WaitFor(
                    renderer,
                    () => renderer.Counters.FramesPresented >= producer.Published,
                    "A published snapshot did not present its frame",
                    _timeout);
            }

            Assert.Equal(1, renderer.RenderStats.Cameras);
            Assert.Equal(1, renderer.RenderStats.NonEmptyCameras);
            Assert.Equal(1, renderer.RenderStats.DrawCalls);
            Assert.Equal(1, renderer.RenderStats.Instances);
            Assert.True(
                renderer.RenderStats.TexturePages >= 2,
                "The windowed scene's texture page did not become resident.");
            Assert.Equal(0, renderer.RenderStats.ValidationErrors);

            Assert.Equal((long)initialSnapshots, renderer.SnapshotCounters.Published);
            Assert.Equal((long)initialSnapshots, renderer.SnapshotCounters.Consumed);
            Assert.True(
                renderer.Counters.FramesPresented >= initialSnapshots,
                $"Only {renderer.Counters.FramesPresented} frames were presented for "
                    + $"{initialSnapshots} consumed snapshots.");
            Assert.True(
                renderer.Counters.FramesPresented <= renderer.SnapshotCounters.Consumed,
                "A frame must correspond to a consumed snapshot (Q7=G1).");

            // The just-submitted frame still holds its snapshot slot: it is released only when that
            // GPU frame index is reused after its fence, or during the stop drain.
            int held = CountRenderingSlots(renderer);
            Assert.InRange(held, 1, 2);
            Assert.Equal(0, CountReadySlots(renderer));
            _output.WriteLine(
                $"after {initialSnapshots} snapshots: {Describe(renderer.Counters)}; "
                    + $"ring={DescribeRing(renderer)}");

            // Capture contract: each captured frame allocates one RGBA8 result shared by every
            // request it serves, and the dimensions are that frame's actual extent. Both requests
            // are queued before the next snapshot, so the same frame serves them.
            long captureSequence = renderer.RenderStats.FrameSequence;
            Task<FrameCaptureResult> firstCapture = renderer.CaptureFrameAsync();
            Task<FrameCaptureResult> secondCapture = renderer.CaptureFrameAsync();
            producer.PublishOne();
            WaitFor(
                renderer,
                () => renderer.RenderStats.FrameSequence > captureSequence,
                "A capture frame was not presented",
                _timeout);

            FrameCaptureResult firstResult = await firstCapture.WaitAsync(_timeout);
            FrameCaptureResult secondResult = await secondCapture.WaitAsync(_timeout);
            RenderStats captureStats = renderer.RenderStats;
            Assert.Equal(captureStats.FramebufferWidth, firstResult.Width);
            Assert.Equal(captureStats.FramebufferHeight, firstResult.Height);
            Assert.Equal(TextureFormat.Rgba8, firstResult.Format);
            Assert.Equal(firstResult.Width * firstResult.Height * 4, firstResult.Pixels.Length);
            Assert.Equal(firstResult.Pixels, secondResult.Pixels);
            Assert.Equal(1, captureStats.DrawCalls);
            _output.WriteLine(
                $"capture: bytes={firstResult.Pixels.Length}, "
                    + $"extent={firstResult.Width}x{firstResult.Height}, "
                    + $"format={firstResult.Format}");

            Resize(window, renderer, producer);
            await AssertCaptureUsesTheResizedExtent(renderer, producer);
            Fullscreen(window, renderer);
            Minimize(window, renderer, producer);

            // Stop with a frame in flight: publish and stop immediately, then join.
            producer.PublishOne();
            long framesAtStop = renderer.Counters.FramesPresented;
            renderer.RequestStop();
            rendererJoined = renderer.Join(_joinTimeout);
            Assert.True(
                rendererJoined,
                $"The windowed renderer did not join within {_joinTimeout.TotalSeconds:0} s.");

            Assert.Equal(RendererStatus.Stopped, renderer.Status);
            Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
            Assert.Null(renderer.FatalException);
            Assert.True(
                renderer.Counters.FramesPresented >= framesAtStop,
                "Stop must not reduce the presented-frame counter.");
            Assert.True(
                renderer.Counters.FramesPresented <= renderer.SnapshotCounters.Consumed,
                "Presented frames must never exceed consumed snapshots (Q7=G1).");
            Assert.True(
                renderer.SnapshotCounters.Consumed <= renderer.SnapshotCounters.Published,
                "Consumed snapshots must never exceed published snapshots.");
            Assert.True(
                renderer.Counters.SwapchainRecreations >= 1,
                "At least one swapchain recreation was expected after resize/fullscreen.");
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Equal(0, instance.ValidationErrorCount);
            Assert.Equal(0, instance.ValidationWarningCount);

            // Join is the quiescent point: every slot is Free, including a slot published after the
            // renderer's cleanup.
            AssertAllSlotsFree(renderer);

            _output.WriteLine($"final counters: {Describe(renderer.Counters)}");
            _output.WriteLine(
                $"final ring: published={renderer.SnapshotCounters.Published}, "
                    + $"consumed={renderer.SnapshotCounters.Consumed}, "
                    + $"superseded={renderer.SnapshotCounters.Superseded}, "
                    + $"backpressureWaits={renderer.SnapshotCounters.BackpressureWaits}, "
                    + $"maxLive={renderer.SnapshotCounters.MaxLive}, "
                    + $"suspended={renderer.SuspendedConsumptions}");
            _output.WriteLine(
                $"instance validation counters: errors={instance.ValidationErrorCount}, "
                    + $"warnings={instance.ValidationWarningCount}");
        }
        finally
        {
            if (renderer is not null)
            {
                renderer.RequestStop();
                rendererJoined = renderer.Join(_joinTimeout) && rendererJoined;
            }

            if (renderer is not null)
            {
                _output.WriteLine(
                    $"pre-cleanup: status={renderer.Status}, stopReason={renderer.StopReason}, "
                        + $"fatal={renderer.FatalException?.Message ?? "(none)"}, "
                        + $"counters={Describe(renderer.Counters)}");
            }

            WriteRendererLog(provider);

            if (rendererJoined)
            {
                if (surface != 0 && instance is not null)
                {
                    instance.DestroySurface(surface);
                }

                // D-M6-09: surface, then instance, then window/SDL; unload the loader after the
                // Vulkan objects are gone.
                instance?.Destroy();
                sdl?.VulkanUnloadLibrary();
                window?.Dispose();
                sdl?.Dispose();
            }
            else
            {
                _output.WriteLine(
                    "The renderer did not join; leaving the host objects alive to avoid an "
                        + "unsafe native teardown.");
            }
        }
    }

    private void Resize(ISdlWindow window, VulkanRenderer renderer, TestSnapshotProducer producer)
    {
        long recreationsBefore = renderer.Counters.SwapchainRecreations;
        long framesBefore = renderer.Counters.FramesPresented;

        ReadOnlySpan<(int Width, int Height)> sizes =
        [
            (960, 600),
            (1280, 720),
            (800, 480),
        ];
        foreach ((int width, int height) in sizes)
        {
            window.SetSize(width, height);
            SdlWindowSize drawable = window.GetVulkanDrawableSize();
            if (drawable.IsEmpty)
            {
                continue;
            }

            renderer.PostWindowExtent(new WindowExtent(drawable.Width, drawable.Height));
        }

        bool recreated = renderer.WaitForProgress(
            counters => counters.SwapchainRecreations > recreationsBefore,
            TimeSpan.FromSeconds(10));
        if (!recreated)
        {
            // Some remote desktop sessions ignore SDL_SetWindowSize; post a distinct extent so the
            // renderer's recreation path is still exercised.
            SdlWindowSize drawable = window.GetVulkanDrawableSize();
            renderer.PostWindowExtent(
                new WindowExtent(
                    drawable.Width > 128 ? drawable.Width - 64 : drawable.Width + 64,
                    drawable.Height > 128 ? drawable.Height - 64 : drawable.Height + 64));
            WaitFor(
                renderer,
                () => renderer.Counters.SwapchainRecreations > recreationsBefore,
                "Resize did not recreate the swapchain",
                _timeout);
        }
        else
        {
            _output.WriteLine($"after resize (bounded wait): {Describe(renderer.Counters)}");
        }

        producer.PublishOne();
        WaitFor(
            renderer,
            () => renderer.Counters.FramesPresented > framesBefore,
            "Presentation did not resume after resize",
            _timeout);
        _output.WriteLine($"after resize: {Describe(renderer.Counters)}");
    }

    private async Task AssertCaptureUsesTheResizedExtent(
        VulkanRenderer renderer,
        TestSnapshotProducer producer)
    {
        long sequence = renderer.RenderStats.FrameSequence;
        Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
        producer.PublishOne();
        WaitFor(
            renderer,
            () => renderer.RenderStats.FrameSequence > sequence,
            "A post-resize capture frame was not presented",
            _timeout);

        FrameCaptureResult result = await capture.WaitAsync(_timeout);
        RenderStats stats = renderer.RenderStats;
        Assert.Equal(stats.FramebufferWidth, result.Width);
        Assert.Equal(stats.FramebufferHeight, result.Height);
        Assert.Equal(TextureFormat.Rgba8, result.Format);
        Assert.Equal(result.Width * result.Height * 4, result.Pixels.Length);
        _output.WriteLine(
            $"post-resize capture: extent={result.Width}x{result.Height}, "
                + $"stats={stats.FramebufferWidth}x{stats.FramebufferHeight}");
    }

    private void Fullscreen(ISdlWindow window, VulkanRenderer renderer)
    {
        long recreationsBefore = renderer.Counters.SwapchainRecreations;
        var entered = false;
        try
        {
            window.SetFullscreen(true);
            entered = true;
        }
        catch (Bork2dException exception)
        {
            _output.WriteLine($"Fullscreen is not supported by this desktop session: {exception.Message}");
            return;
        }

        try
        {
            SdlWindowSize fullscreenDrawable = window.GetVulkanDrawableSize();
            renderer.PostFullscreen(true);
            if (!fullscreenDrawable.IsEmpty)
            {
                renderer.PostWindowExtent(
                    new WindowExtent(fullscreenDrawable.Width, fullscreenDrawable.Height));
            }

            WaitFor(
                renderer,
                () => renderer.Counters.SwapchainRecreations > recreationsBefore,
                "Entering fullscreen did not recreate the swapchain",
                _timeout);

            window.SetFullscreen(false);
            SdlWindowSize restoredDrawable = window.GetVulkanDrawableSize();
            renderer.PostFullscreen(false);
            if (!restoredDrawable.IsEmpty)
            {
                renderer.PostWindowExtent(
                    new WindowExtent(restoredDrawable.Width, restoredDrawable.Height));
            }

            WaitFor(
                renderer,
                () => renderer.Counters.SwapchainRecreations > recreationsBefore,
                "Leaving fullscreen did not recreate the swapchain",
                _timeout);
            _output.WriteLine($"after fullscreen: {Describe(renderer.Counters)}");
        }
        finally
        {
            if (entered)
            {
                try
                {
                    window.SetFullscreen(false);
                }
                catch (Bork2dException)
                {
                    // Best effort; the session is torn down after the renderer joins.
                }
            }
        }
    }

    private void Minimize(
        ISdlWindow window,
        VulkanRenderer renderer,
        TestSnapshotProducer producer)
    {
        long suspendedBefore = renderer.SuspendedConsumptions;
        long framesBefore = renderer.Counters.FramesPresented;
        long consumedBefore = renderer.SnapshotCounters.Consumed;

        renderer.PostMinimized(true);

        // The suspended consumer drains the newest snapshot without a GPU frame.
        for (var index = 0; index < 3; index++)
        {
            producer.PublishOne();
            long expected = suspendedBefore + index + 1;
            WaitFor(
                renderer,
                () => renderer.SuspendedConsumptions >= expected,
                "The renderer did not drain a snapshot while minimized",
                _timeout);
        }

        Assert.Equal(framesBefore, renderer.Counters.FramesPresented);
        Assert.Equal(consumedBefore, renderer.SnapshotCounters.Consumed);
        Assert.True(
            renderer.SuspendedConsumptions >= suspendedBefore + 3,
            "The suspended drain must count each dropped snapshot (Q8=H1).");

        renderer.PostMinimized(false);
        SdlWindowSize drawable = window.GetVulkanDrawableSize();
        if (!drawable.IsEmpty)
        {
            renderer.PostWindowExtent(new WindowExtent(drawable.Width, drawable.Height));
        }

        producer.PublishOne();
        WaitFor(
            renderer,
            () => renderer.Counters.FramesPresented > framesBefore,
            "Presentation did not resume after restoring the window",
            _timeout);
        Assert.True(
            renderer.SnapshotCounters.Consumed > consumedBefore,
            "Restoring the window must resume one frame per snapshot.");
        _output.WriteLine(
            $"after minimize/restore: {Describe(renderer.Counters)}; "
                + $"suspended={renderer.SuspendedConsumptions}");
    }

    private void WaitFor(
        VulkanRenderer renderer,
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

    private static int CountRenderingSlots(VulkanRenderer renderer)
    {
        var count = 0;
        foreach (var slot in renderer.SnapshotRing.Slots)
        {
            if (slot.State == SnapshotSlotState.Rendering)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountReadySlots(VulkanRenderer renderer)
    {
        var count = 0;
        foreach (var slot in renderer.SnapshotRing.Slots)
        {
            if (slot.State == SnapshotSlotState.Ready)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertAllSlotsFree(VulkanRenderer renderer)
    {
        foreach (var slot in renderer.SnapshotRing.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, renderer.SnapshotRing.LatestIndex);
    }

    private static string DescribeRing(VulkanRenderer renderer)
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
        => $"status=windowed, framesPresented={counters.FramesPresented}, "
            + $"recreations={counters.SwapchainRecreations}, "
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
            || message.Contains("No sRGB", StringComparison.Ordinal);
    }

    /// <summary>
    /// The test producer: fills one slot through the renderer's internal slot API and publishes it.
    /// </summary>
    private sealed class TestSnapshotProducer
    {
        private readonly VulkanRenderer _renderer;
        private readonly SpriteRenderData _sprite;
        private long _published;

        internal TestSnapshotProducer(VulkanRenderer renderer, SpriteAsset sprite)
        {
            _renderer = renderer;
            _sprite = SpriteRenderData.Freeze(sprite);
        }

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
            slot.Snapshot.AddCamera(
                new CameraSnapshot(
                    ZIndex: 0,
                    WorldMatrix: Matrix3x2.Identity,
                    Size: 8f,
                    Viewport: new Vector4(0f, 0f, 1f, 1f),
                    CullingMask: 1u,
                    Clear: ClearKind.Color,
                    ClearColor: new Color(0.05f, 0.1f, 0.2f, 1f)));
            slot.Snapshot.AddSprite(
                new SpriteSnapshot(
                    _sprite,
                    Matrix3x2.Identity,
                    Color.White,
                    Layer: 1u,
                    ZIndex: 0,
                    FlipX: false,
                    FlipY: false));
            _renderer.PublishSnapshot(slot);
        }
    }
}
