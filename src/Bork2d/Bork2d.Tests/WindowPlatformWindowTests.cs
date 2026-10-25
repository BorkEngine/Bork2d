using System.Numerics;
using Bork2d.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Bork2d.Tests;

/// <summary>
/// Opt-in end-to-end tests for the real windowed platform. They need a desktop session and the
/// real SDL2 native library, so they are skipped unless <c>BORK2D_RUN_WINDOW_TESTS=1</c>.
/// </summary>
/// <remarks>
/// The tests open a real window for a few milliseconds but never depend on a game loop: they start
/// the platform, observe confirmed state, and shut it down. CI never sets the environment variable.
/// The class shares the <c>RealSdlWindow</c> collection with the seam smoke tests because SDL video
/// is process-global and must not be initialized concurrently.
/// </remarks>
[Collection("RealSdlWindow")]
public sealed class WindowPlatformWindowTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="WindowPlatformWindowTests"/> class.</summary>
    /// <param name="output">The xUnit test output sink.</param>
    public WindowPlatformWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [WindowFact]
    public void RealWindowPlatformStartsVisibleWithTheFramebufferDpiContract()
    {
        using var sdl = new SilkSdlApi();
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(title: "Bork2d WP2 real window"),
            () => sdl,
            NullLogger.Instance);

        try
        {
            platform.Start();
            var state = platform.State;

            _output.WriteLine($"SDL version: {sdl.Version}");
            _output.WriteLine($"video driver: {sdl.VideoDriver}");
            _output.WriteLine($"window points: {state.WindowPoints}");
            _output.WriteLine($"framebuffer pixels: {state.FramebufferSize}");
            _output.WriteLine($"scale: {state.Scale:F4}");
            _output.WriteLine(
                $"visible={state.Visible}, focused={state.Focused}, minimized={state.Minimized}");

            Assert.True(state.Visible, "the window must be visible at start.");
            Assert.True(state.Alive);
            Assert.Equal("Bork2d WP2 real window", state.Title);
            Assert.False(state.FramebufferSize.IsEmpty);
            Assert.Equal(1280, state.FramebufferSize.Width);
            Assert.Equal(720, state.FramebufferSize.Height);
            Assert.InRange(state.Scale, 1.49f, 1.51f);
            Assert.InRange(state.WindowPoints.Width, 852, 854);
            Assert.InRange(state.WindowPoints.Height, 479, 481);
            Assert.False(state.Fullscreen);
            Assert.True(state.CursorVisible);
            Assert.False(state.Minimized);

            platform.Shutdown();

            Assert.False(platform.State.Alive, "shutdown must publish an unavailable window.");
        }
        finally
        {
            platform.Dispose();
        }
    }

    [WindowFact]
    public void RealWindowPlatformTitleFullscreenCursorAndMinimizeRoundTrip()
    {
        using var sdl = new SilkSdlApi();
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(title: "Bork2d WP2 controls"),
            () => sdl,
            NullLogger.Instance);
        var proxy = new WindowProxy();

        try
        {
            platform.Start();
            proxy.Attach(platform);

            var windowedSize = proxy.Size;
            var windowedPoints = platform.State.WindowPoints;
            Assert.False(proxy.IsMinimized);
            Assert.True(proxy.CursorVisible);

            proxy.Title = "Bork2d WP2 renamed";
            WindowTestSupport.WaitUntil(platform, () => proxy.Title == "Bork2d WP2 renamed");
            Assert.Equal("Bork2d WP2 renamed", platform.State.Title);

            proxy.CursorVisible = false;
            WindowTestSupport.WaitUntil(platform, () => !proxy.CursorVisible);
            proxy.CursorVisible = true;
            WindowTestSupport.WaitUntil(platform, () => proxy.CursorVisible);

            proxy.Fullscreen = true;
            WindowTestSupport.WaitUntil(platform, () => proxy.Fullscreen);
            _output.WriteLine(
                $"fullscreen framebuffer: {platform.State.FramebufferSize}; points: {platform.State.WindowPoints}");
            Assert.False(platform.State.FramebufferSize.IsEmpty);

            proxy.Fullscreen = false;
            WindowTestSupport.WaitUntil(platform, () => !proxy.Fullscreen);
            _output.WriteLine(
                $"restored framebuffer: {platform.State.FramebufferSize}; points: {platform.State.WindowPoints}");

            Assert.False(platform.State.FramebufferSize.IsEmpty);
            Assert.Equal(windowedSize, proxy.Size);
            Assert.Equal(windowedPoints, platform.State.WindowPoints);
            Assert.False(proxy.IsMinimized);

            proxy.Detach();
            platform.Shutdown();
        }
        finally
        {
            proxy.Detach();
            platform.Dispose();
        }
    }

    [WindowFact]
    public void RealWindowPlatformSetsBothDpiHintsBeforeSdlInit()
    {
        using var inner = new SilkSdlApi();
        using var recording = new RecordingSdlApi(inner);
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(title: "Bork2d WP2 DPI hints"),
            () => recording,
            NullLogger.Instance);

        try
        {
            platform.Start();

            var calls = recording.Calls;
            foreach (var call in calls)
            {
                _output.WriteLine(call);
            }

            var awarenessIndex = calls.ToList().IndexOf(
                $"SetHint({SdlHints.WindowsDpiAwareness}={SdlHints.PerMonitorV2})");
            var scalingIndex = calls.ToList().IndexOf(
                $"SetHint({SdlHints.WindowsDpiScaling}={SdlHints.DpiScalingEnabled})");
            var initIndex = calls.ToList().IndexOf("InitializeVideo");
            var createIndex = calls.ToList().FindIndex(
                call => call.StartsWith("CreateWindow(", StringComparison.Ordinal));

            Assert.True(awarenessIndex >= 0, "SDL_WINDOWS_DPI_AWARENESS was not set.");
            Assert.True(scalingIndex >= 0, "SDL_WINDOWS_DPI_SCALING was not set.");
            Assert.True(awarenessIndex < initIndex, "DPI awareness must precede SDL_Init.");
            Assert.True(scalingIndex < initIndex, "DPI scaling must precede SDL_Init.");
            Assert.True(initIndex < createIndex, "SDL_Init must precede window creation.");
        }
        finally
        {
            platform.Dispose();
        }
    }
}
