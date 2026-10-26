using System.Numerics;
using Bork2d.Api;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Platform.Sdl;

/// <summary>
/// Deterministic tests for the OS/window-thread platform: creation, the command/state handoff,
/// event handling, exit requests, fatal errors, and shutdown ordering.
/// </summary>
public sealed class SdlWindowPlatformTests
{
    [Fact]
    public void StartCreatesOneVisibleHighDpiVulkanWindowWithDpiHintsBeforeInit()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var window = Assert.Single(sdl.Windows);
        var state = platform.State;

        Assert.True(sdl.VideoInitialized);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.InitializeVideoThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, sdl.CreateWindowThreadId);

        Assert.Equal("Bork2d", window.Title);
        Assert.Equal(SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi | SdlWindowFlags.Resizable, window.Flags & ~SdlWindowFlags.Shown);
        Assert.True(window.Flags.HasFlag(SdlWindowFlags.Shown));

        Assert.Equal("Bork2d", state.Title);
        Assert.Equal(new SdlWindowSize(1280, 720), state.WindowPoints);
        Assert.Equal(new SdlWindowSize(1280, 720), state.FramebufferSize);
        Assert.Equal(1f, state.Scale);
        Assert.False(state.Fullscreen);
        Assert.True(state.CursorVisible);
        Assert.False(state.Minimized);
        Assert.True(state.Visible);
        Assert.True(state.Alive);
        Assert.Equal(0, window.SetSizeCallCount);

        Assert.Equal(
            [
                (SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2),
                (SdlHints.WindowsDpiScaling, SdlHints.DpiScalingEnabled),
            ],
            sdl.Hints);

        var calls = sdl.Calls.ToList();
        var awarenessIndex = calls.IndexOf($"SetHint({SdlHints.WindowsDpiAwareness}={SdlHints.PerMonitorV2})");
        var scalingIndex = calls.IndexOf($"SetHint({SdlHints.WindowsDpiScaling}={SdlHints.DpiScalingEnabled})");
        var initIndex = calls.IndexOf("InitializeVideo");
        var createIndex = calls.FindIndex(call => call.StartsWith("CreateWindow(", StringComparison.Ordinal));

        Assert.True(awarenessIndex >= 0, "the DPI awareness hint was not set.");
        Assert.True(scalingIndex >= 0, "the DPI scaling hint was not set.");
        Assert.True(initIndex >= 0, "SDL video was not initialized.");
        Assert.True(createIndex >= 0, "no window was created.");
        Assert.True(awarenessIndex < initIndex, "DPI awareness must precede SDL_Init.");
        Assert.True(scalingIndex < initIndex, "DPI scaling must precede SDL_Init.");
        Assert.True(initIndex < createIndex, "SDL_Init must precede window creation.");
    }

    [Fact]
    public void StartAdjustsPointSizeOnceUsingTheMeasuredScale()
    {
        var sdl = new FakeSdlApi { WindowScale = 1.5f };
        using var platform = WindowTestSupport.Start(sdl);
        var window = Assert.Single(sdl.Windows);
        var state = platform.State;

        Assert.Equal(1, window.SetSizeCallCount);
        Assert.Equal(853, window.Size.Width);
        Assert.Equal(480, window.Size.Height);
        Assert.Equal(new SdlWindowSize(1280, 720), state.FramebufferSize);
        Assert.Equal(new SdlWindowSize(853, 480), state.WindowPoints);
        Assert.InRange(state.Scale, 1.49f, 1.51f);
    }

    [Fact]
    public void FullscreenAtCreationIgnoresTheInitialFramebufferSize()
    {
        var sdl = new FakeSdlApi { WindowScale = 1.5f };
        using var platform = WindowTestSupport.Start(
            sdl,
            WindowTestSupport.Options(fullscreen: true));
        var window = Assert.Single(sdl.Windows);

        Assert.True(window.Flags.HasFlag(SdlWindowFlags.FullscreenDesktop));
        Assert.True(platform.State.Fullscreen);
        Assert.Equal(0, window.SetSizeCallCount);
    }

    [Fact]
    public void StartFailureIsABork2dExceptionWithSdlErrorAndCleansUp()
    {
        var sdl = new FakeSdlApi
        {
            ErrorText = "video subsystem boom",
            InitializeFailure = "SDL_Init(SDL_INIT_VIDEO) failed: video subsystem boom",
        };
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var thrown = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("video subsystem boom", thrown.Message, StringComparison.Ordinal);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.Empty(sdl.Windows);
        Assert.True(sdl.Hints.Count == 2);
        platform.Dispose();
        platform.Dispose();
    }

    [Fact]
    public void WindowCreationFailureIsABork2dExceptionWithSdlError()
    {
        var sdl = new FakeSdlApi
        {
            ErrorText = "no display",
            CreateWindowFailure = "SDL_CreateWindow failed: no display",
        };
        var platform = new SdlWindowPlatform(
            WindowTestSupport.Options(),
            () => sdl,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var thrown = Assert.Throws<Bork2dException>(platform.Start);

        Assert.Contains("no display", thrown.Message, StringComparison.Ordinal);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.Empty(sdl.Windows);
        platform.Dispose();
    }

    [Fact]
    public void CommandsApplyInQueuedOrderAndPublishTheConfirmedState()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);

        proxy.Title = "renamed";
        proxy.Size = new Vector2(800f, 600f);
        proxy.CursorVisible = false;
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "renamed");
        WindowTestSupport.WaitUntil(platform, () => proxy.Size == new Vector2(800f, 600f));
        WindowTestSupport.WaitUntil(platform, () => !proxy.CursorVisible);

        Assert.Equal("renamed", platform.State.Title);
        Assert.Equal(new SdlWindowSize(800, 600), platform.State.FramebufferSize);
        Assert.False(sdl.CursorVisible);

        proxy.Fullscreen = true;
        WindowTestSupport.WaitUntil(platform, () => proxy.Fullscreen);

        Assert.True(platform.State.Fullscreen);
        Assert.True(Assert.Single(sdl.Windows).IsFullscreen);

        proxy.Fullscreen = false;
        WindowTestSupport.WaitUntil(platform, () => !proxy.Fullscreen);

        Assert.False(platform.State.Fullscreen);
    }

    [Fact]
    public void CommandsMayBeEnqueuedFromAnotherThread()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);

        var thread = new Thread(() =>
        {
            proxy.Title = "from-worker";
            proxy.Size = new Vector2(1024f, 768f);
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The command producer did not finish.");

        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "from-worker");
        WindowTestSupport.WaitUntil(platform, () => proxy.Size == new Vector2(1024f, 768f));

        Assert.Equal("from-worker", platform.State.Title);
        Assert.Equal(new SdlWindowSize(1024, 768), platform.State.FramebufferSize);
    }

    [Fact]
    public void SizeRequestsAreIgnoredWhileFullscreen()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl, WindowTestSupport.Options(fullscreen: true));
        var window = Assert.Single(sdl.Windows);
        var confirmedSize = platform.State.FramebufferSize;

        platform.SetSize(800f, 600f);
        platform.SetTitle("after-ignored-size");
        WindowTestSupport.WaitUntil(platform, () => platform.State.Title == "after-ignored-size");

        Assert.Equal(confirmedSize, platform.State.FramebufferSize);
        Assert.Equal(0, window.SetSizeCallCount);
    }

    [Fact]
    public void BoundedWaitHelpersReturnFalseWhenNothingIsPublished()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var version = platform.StateVersion;

        Assert.False(platform.WaitForStateChange(version, TimeSpan.FromMilliseconds(20)));
        Assert.False(platform.WaitForExitRequest(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(version, platform.StateVersion);
        Assert.False(platform.ExitRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseOrQuitRequestsExitWithoutDestroyingTheWindow(bool close)
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var window = Assert.Single(sdl.Windows);

        sdl.Enqueue(close
            ? SdlEvent.Window(SdlEventType.WindowClose)
            : SdlEvent.Quit());

        WindowTestSupport.WaitForExitRequest(platform);

        Assert.True(platform.ExitRequested);
        Assert.Equal(WindowStopReason.Close, platform.StopReason);
        Assert.True(platform.State.Alive);
        Assert.False(window.IsDisposed);

        platform.Shutdown();

        Assert.True(window.IsDisposed);
        Assert.False(platform.State.Alive);
    }

    [Fact]
    public void FatalSdlErrorIsCapturedAndRequestsErrorExit()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);
        window.SetFullscreenFailure = "SDL_SetWindowFullscreen failed: boom";
        sdl.ErrorText = "boom";

        proxy.Fullscreen = true;
        WindowTestSupport.WaitForExitRequest(platform);
        platform.Shutdown();

        var fatal = Assert.IsType<Bork2dException>(platform.FatalException);
        Assert.Contains("boom", fatal.Message, StringComparison.Ordinal);
        Assert.Equal(WindowStopReason.Error, platform.StopReason);
        Assert.True(sdl.Disposed);
        Assert.True(window.IsDisposed);
        Assert.False(platform.State.Alive);
    }

    [Fact]
    public void ShutdownIsIdempotentAndDestroysTheWindowBeforeQuittingSdl()
    {
        var sdl = new FakeSdlApi();
        var platform = WindowTestSupport.Start(sdl);
        var window = Assert.Single(sdl.Windows);

        platform.Shutdown();
        platform.Shutdown();

        Assert.True(window.IsDisposed);
        Assert.True(sdl.QuitCalled);
        Assert.True(sdl.Disposed);
        Assert.False(platform.State.Alive);

        var calls = sdl.Calls.ToList();
        var destroyIndex = calls.IndexOf("DestroyWindow");
        var quitIndex = calls.LastIndexOf("Quit");
        var disposeIndex = calls.IndexOf("Dispose");

        Assert.True(destroyIndex >= 0, "the window was not destroyed.");
        Assert.True(quitIndex >= 0, "SDL video was not quit.");
        Assert.True(disposeIndex >= 0, "the SDL adapter was not disposed.");
        Assert.True(destroyIndex < quitIndex, "the window must be destroyed before SDL_Quit.");
        Assert.True(quitIndex < disposeIndex, "SDL_Quit must precede adapter disposal.");

        platform.Dispose();
    }

    [Fact]
    public void WindowEventsUpdateConfirmedStateInOrder()
    {
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        window.Resize(1024, 768);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 1024, 768));
        WindowTestSupport.WaitUntil(platform, () => proxy.Size == new Vector2(1024f, 768f));

        window.ChangeScale(1.5f);
        window.Resize(800, 600);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 800, 600));
        WindowTestSupport.WaitUntil(platform, () => proxy.Size == new Vector2(1200f, 900f));
        Assert.Equal(1.5f, platform.State.Scale);

        window.SetMinimized(true);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
        WindowTestSupport.WaitUntil(platform, () => proxy.IsMinimized);
        Assert.Equal(new Vector2(1200f, 900f), proxy.Size);

        window.Resize(0, 0);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 0, 0));
        proxy.Title = "after-zero-while-minimized";
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "after-zero-while-minimized");
        Assert.Equal(new Vector2(1200f, 900f), proxy.Size);

        window.SetMinimized(false);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
        WindowTestSupport.WaitUntil(platform, () => !proxy.IsMinimized);
        Assert.Equal(new Vector2(1200f, 900f), proxy.Size);

        window.SetFocused(true);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusGained));
        WindowTestSupport.WaitUntil(platform, () => platform.State.Focused);

        window.SetFocused(false);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusLost));
        WindowTestSupport.WaitUntil(platform, () => !platform.State.Focused);

        window.SetMaximized();
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMaximized));
        WindowTestSupport.WaitUntil(platform, () => !proxy.IsMinimized);

        window.SetFullscreen(true);
        window.Resize(1600, 900);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 1600, 900));
        WindowTestSupport.WaitUntil(platform, () => proxy.Fullscreen);
        Assert.Equal(1.5f, platform.State.Scale);
        Assert.Equal(new Vector2(2400f, 1350f), proxy.Size);
    }
}
