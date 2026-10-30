using System.Runtime.InteropServices;
using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
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
/// Opt-in real-GPU proof for the windowed renderer: SDL window and surface, device and swapchain,
/// clears presented continuously, resize/fullscreen/minimize, and a bounded stop.
/// </summary>
/// <remarks>
/// The test thread acts as the OS/window thread: it creates the SDL window, the instance, and the
/// surface, then starts the renderer and observes its counters with event-based bounded waits. It
/// skips (rather than fails) when the Vulkan loader, desktop session, device, or validation layer
/// is unavailable (D-M6-05, D-M6-20). No sleep, no spin, no readback (D-M6-23).
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
    public void WindowedRendererPresentsClearsResizesMinimizesAndStopsCleanly()
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

            _output.WriteLine(
                $"window={window.Size}, drawable={drawable}, validation=on");

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
            WaitFor(
                renderer,
                counters => counters.FramesPresented >= 30,
                "The renderer presented fewer than 30 frames",
                _timeout);
            _output.WriteLine($"after 30+ frames: {Describe(renderer.Counters)}");

            Resize(window, renderer);
            Fullscreen(window, renderer);
            Minimize(window, renderer);

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
                renderer.Counters.FramesPresented >= 30,
                $"Only {renderer.Counters.FramesPresented} frames were presented.");
            Assert.True(
                renderer.Counters.FramesPresented >= framesAtStop,
                "Stop must not reduce the presented-frame counter.");
            Assert.True(
                renderer.Counters.SwapchainRecreations >= 1,
                "At least one swapchain recreation was expected after resize/fullscreen.");
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Equal(0, instance.ValidationErrorCount);
            Assert.Equal(0, instance.ValidationWarningCount);

            _output.WriteLine($"final counters: {Describe(renderer.Counters)}");
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

    private void Resize(ISdlWindow window, VulkanRenderer renderer)
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
                counters => counters.SwapchainRecreations > recreationsBefore,
                "Resize did not recreate the swapchain",
                _timeout);
        }
        else
        {
            _output.WriteLine($"after resize (bounded wait): {Describe(renderer.Counters)}");
        }

        WaitFor(
            renderer,
            counters => counters.FramesPresented > framesBefore,
            "Presentation did not resume after resize",
            _timeout);
        _output.WriteLine($"after resize: {Describe(renderer.Counters)}");
    }

    private void Fullscreen(ISdlWindow window, VulkanRenderer renderer)
    {
        long recreationsBefore = renderer.Counters.SwapchainRecreations;
        bool entered;
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
                counters => counters.SwapchainRecreations > recreationsBefore,
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
                counters => counters.SwapchainRecreations > recreationsBefore,
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

    private void Minimize(ISdlWindow window, VulkanRenderer renderer)
    {
        long skipsBefore = renderer.MinimizedSkips;
        long framesBefore = renderer.Counters.FramesPresented;

        renderer.PostMinimized(true);
        WaitFor(
            renderer,
            _ => renderer.MinimizedSkips > skipsBefore,
            "The renderer did not observe the minimized state",
            _timeout);

        renderer.PostMinimized(false);
        SdlWindowSize drawable = window.GetVulkanDrawableSize();
        if (!drawable.IsEmpty)
        {
            renderer.PostWindowExtent(new WindowExtent(drawable.Width, drawable.Height));
        }

        WaitFor(
            renderer,
            counters => counters.FramesPresented > framesBefore,
            "Presentation did not resume after restoring the window",
            _timeout);
        _output.WriteLine($"after minimize/restore: {Describe(renderer.Counters)}");
    }

    private void WaitFor(
        VulkanRenderer renderer,
        Func<RendererCounters, bool> predicate,
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
            $"{message} within {timeout.TotalSeconds:0} s. {Describe(renderer.Counters)}.");
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
                StringComparison.Ordinal);
    }
}
