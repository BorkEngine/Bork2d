using Bork2d.Api;
using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// The internal SDL seam is complete enough for an in-memory fake to drive window creation,
/// commands, event order, focus/minimize/resize transitions, and input state without SDL.
/// </summary>
/// <remarks>
/// These tests exercise the seam contract, not SDL. Later work packages reuse
/// <see cref="FakeSdlApi"/> for the window platform and input accumulator suites.
/// </remarks>
public sealed class SdlSeamTests
{
    [Fact]
    public void InMemoryFakeDrivesWindowCreationCommandsAndInput()
    {
        var api = new FakeSdlApi();
        Assert.True(api.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2));
        api.InitializeVideo();

        Assert.True(api.VideoInitialized);
        Assert.Equal(
            new[] { (SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2) },
            api.Hints);
        using var window = (FakeSdlWindow)api.CreateWindow(
            "Bork2d",
            1280,
            720,
            SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi | SdlWindowFlags.Resizable);

        Assert.Equal("Bork2d", window.Title);
        Assert.Equal(1280, window.Size.Width);
        Assert.Equal(720, window.Size.Height);
        Assert.Equal(1280, window.SizeInPixels.Width);
        Assert.Equal(720, window.SizeInPixels.Height);
        Assert.Equal(
            SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi | SdlWindowFlags.Resizable
                | SdlWindowFlags.Shown,
            window.Flags);

        window.SetTitle("Renamed");
        window.SetSize(800, 600);
        window.SetFullscreen(true);

        Assert.Equal("Renamed", window.Title);
        Assert.Equal(800, window.Size.Width);
        Assert.Equal(600, window.Size.Height);
        Assert.True(window.IsFullscreen);
        Assert.True(window.Flags.HasFlag(SdlWindowFlags.FullscreenDesktop));

        window.SetFullscreen(false);
        Assert.False(window.IsFullscreen);

        api.SetKey(4, isDown: true);
        api.MouseState = new SdlMouseState(10, 20, buttons: 1);

        Assert.Equal(1, api.GetKeyboardState()[4]);
        Assert.Equal(0, api.GetKeyboardState()[5]);
        Assert.Equal(10, api.GetMouseState().X);
        Assert.Equal(20, api.GetMouseState().Y);
        Assert.Equal(1u, api.GetMouseState().Buttons);

        api.ShowCursor(false);
        Assert.False(api.CursorVisible);
        api.StartTextInput();
        Assert.True(api.TextInputActive);
        api.StopTextInput();
        Assert.False(api.TextInputActive);

        Assert.Equal(0x8000, api.RegisterEvents(2));
        Assert.Equal(0x8002, api.RegisterEvents(1));

        window.Dispose();
        Assert.True(window.IsDisposed);
        api.Dispose();
        Assert.True(api.QuitCalled);
    }

    [Fact]
    public void InMemoryFakePreservesEventOrderAcrossFocusMinimizeAndResize()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();

        api.Enqueue(SdlEvent.Key(SdlEventType.KeyDown, 4));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusGained));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusLost));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowResized, 1024, 768));
        api.Enqueue(SdlEvent.Window(SdlEventType.WindowSizeChanged, 1024, 768));
        api.Enqueue(SdlEvent.TextInput("A"));
        api.Enqueue(SdlEvent.User(0x8000));
        api.Enqueue(SdlEvent.Quit());

        var events = new List<SdlEvent>();
        while (api.TryPollEvent(out var sdlEvent))
        {
            events.Add(sdlEvent);
        }

        Assert.Equal(
            new[]
            {
                SdlEventType.KeyDown,
                SdlEventType.WindowFocusGained,
                SdlEventType.WindowMinimized,
                SdlEventType.WindowRestored,
                SdlEventType.WindowFocusLost,
                SdlEventType.WindowResized,
                SdlEventType.WindowSizeChanged,
                SdlEventType.TextInput,
                SdlEventType.UserEvent,
                SdlEventType.Quit,
            },
            events.Select(sdlEvent => sdlEvent.Type));
        Assert.Equal(1024, events[5].Width);
        Assert.Equal(768, events[5].Height);
        Assert.Equal("A", events[7].Text);
        Assert.Equal(0x8000, events[8].UserCode);
        Assert.False(api.TryPollEvent(out _));
    }

    [Fact]
    public void FakeCloseAndWakeupEventsCanBePushed()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();

        Assert.True(api.TryPushEvent(SdlEvent.User(0x8000)));
        Assert.True(api.TryPushEvent(SdlEvent.Quit()));

        Assert.True(api.TryWaitEvent(out var wakeup));
        Assert.True(api.TryWaitEvent(out var quit));
        Assert.Equal(SdlEventType.UserEvent, wakeup.Type);
        Assert.Equal(0x8000, wakeup.UserCode);
        Assert.Equal(SdlEventType.Quit, quit.Type);
        Assert.False(api.TryWaitEvent(out _));
    }

    [Fact]
    public void DisposedFakeRejectsFurtherUse()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();

        api.Dispose();

        Assert.Throws<ObjectDisposedException>(() => api.GetError());
        Assert.Throws<ObjectDisposedException>(() => api.CreateWindow("x", 1, 1, SdlWindowFlags.None));
        Assert.Throws<ObjectDisposedException>(() => api.GetKeyboardState());
    }

    [Fact]
    public void DisposedFakeWindowRejectsFurtherCommands()
    {
        var api = new FakeSdlApi();
        api.InitializeVideo();
        var window = api.CreateWindow("x", 320, 200, SdlWindowFlags.None);

        window.Dispose();
        window.Dispose();

        Assert.Throws<ObjectDisposedException>(() => window.SetTitle("y"));
        Assert.Throws<ObjectDisposedException>(() => window.SetSize(1, 1));
        Assert.Throws<ObjectDisposedException>(() => window.SetFullscreen(true));
    }

    [Fact]
    public void FakeWindowScaleConvertsPointsToFramebufferPixels()
    {
        var api = new FakeSdlApi { WindowScale = 1.5f };
        api.InitializeVideo();
        using var window = (FakeSdlWindow)api.CreateWindow("x", 1280, 720, SdlWindowFlags.AllowHighDpi);

        Assert.Equal(1280, window.Size.Width);
        Assert.Equal(1920, window.SizeInPixels.Width);
        Assert.Equal(1080, window.SizeInPixels.Height);

        window.SetSize(800, 600);

        Assert.Equal(1200, window.SizeInPixels.Width);
        Assert.Equal(900, window.SizeInPixels.Height);
    }

    [Fact]
    public void FakeSurfacesConfiguredFailuresAsBork2dExceptions()
    {
        var api = new FakeSdlApi { InitializeFailure = "SDL_Init(SDL_INIT_VIDEO) failed: test" };

        var init = Assert.Throws<Bork2dException>(api.InitializeVideo);
        Assert.Contains("SDL_Init", init.Message, StringComparison.Ordinal);

        var failingWindowApi = new FakeSdlApi { CreateWindowFailure = "SDL_CreateWindow failed: test" };
        failingWindowApi.InitializeVideo();

        var window = Assert.Throws<Bork2dException>(
            () => failingWindowApi.CreateWindow("x", 1, 1, SdlWindowFlags.None));
        Assert.Contains("SDL_CreateWindow", window.Message, StringComparison.Ordinal);
    }
}
