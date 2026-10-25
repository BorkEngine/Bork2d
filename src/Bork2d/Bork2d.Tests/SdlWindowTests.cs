using Bork2d.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Bork2d.Tests;

/// <summary>
/// Opt-in smoke tests against the real SDL2 native library and a desktop session.
/// </summary>
/// <remarks>
/// Every test is skipped unless <c>BORK2D_RUN_WINDOW_TESTS=1</c>. CI never runs these, so it never
/// initializes SDL video or opens a window. The tests stay small on purpose: the functional proof
/// remains the windowed AOT demo (M2 WP5).
/// </remarks>
public sealed class SdlWindowTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="SdlWindowTests"/> class.</summary>
    /// <param name="output">The xUnit test output sink.</param>
    public SdlWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [WindowFact]
    public void RealSdlCreatesAndDestroysAVisibleHighDpiVulkanWindow()
    {
        using var sdl = new SilkSdlApi();
        _output.WriteLine($"SDL version: {sdl.Version}");
        Assert.True(
            sdl.Version.Major == 2 && sdl.Version.Minor >= 26,
            $"SDL {sdl.Version} is older than 2.26, which M2 requires for SDL_GetWindowSizeInPixels.");

        Assert.True(sdl.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2));
        sdl.InitializeVideo();

        using var window = sdl.CreateWindow(
            "Bork2d SDL window test",
            1280,
            720,
            SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi | SdlWindowFlags.Resizable);

        var windowPoints = window.Size;
        var framebufferPixels = window.SizeInPixels;
        var scaleX = (double)framebufferPixels.Width / windowPoints.Width;
        var scaleY = (double)framebufferPixels.Height / windowPoints.Height;

        _output.WriteLine($"window points: {windowPoints}");
        _output.WriteLine($"framebuffer pixels: {framebufferPixels}");
        _output.WriteLine($"scale: {scaleX:F4} x {scaleY:F4}");
        _output.WriteLine($"flags: 0x{(uint)window.Flags:X8}");

        Assert.False(windowPoints.IsEmpty);
        Assert.False(framebufferPixels.IsEmpty);
        Assert.True(framebufferPixels.Width >= windowPoints.Width);
        Assert.True(scaleX > 0);
        Assert.True(scaleY > 0);
        Assert.True(window.Flags.HasFlag(SdlWindowFlags.Vulkan));
        Assert.True(window.Flags.HasFlag(SdlWindowFlags.AllowHighDpi));
        Assert.True(window.Flags.HasFlag(SdlWindowFlags.Shown));

        window.SetTitle("Bork2d SDL window test (renamed)");
        window.SetSize(1024, 600);
        Assert.False(window.Size.IsEmpty);

        sdl.ShowCursor(false);
        sdl.ShowCursor(true);
        sdl.StartTextInput();
        sdl.StopTextInput();

        Assert.False(sdl.GetKeyboardState().IsEmpty);
        var firstUserEvent = sdl.RegisterEvents(1);
        Assert.True(firstUserEvent >= 0x8000, $"SDL_RegisterEvents returned {firstUserEvent}.");

        var mouse = sdl.GetMouseState();
        _output.WriteLine($"mouse: x={mouse.X}, y={mouse.Y}, buttons=0x{mouse.Buttons:X}");

        Assert.True(sdl.TryPushEvent(SdlEvent.Quit()), "SDL_PushEvent(SDL_QUIT) must succeed.");

        var pumped = 0;
        var sawQuit = false;
        while (pumped < 1024 && sdl.TryPollEvent(out var sdlEvent))
        {
            pumped++;
            sawQuit = sdlEvent.Type == SdlEventType.Quit;
            if (sawQuit)
            {
                break;
            }
        }

        _output.WriteLine($"pumped events: {pumped}");
        Assert.True(sawQuit, "the pushed SDL_QUIT event must come back through the seam.");
    }

    [WindowFact]
    public void RealSdlApiDisposeIsIdempotentAndRejectsFurtherUse()
    {
        var sdl = new SilkSdlApi();
        _output.WriteLine($"SDL version: {sdl.Version}");
        sdl.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2);
        sdl.InitializeVideo();

        sdl.Quit();
        sdl.Quit();
        sdl.Dispose();
        sdl.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sdl.GetError());
        Assert.Throws<ObjectDisposedException>(() => sdl.InitializeVideo());
    }

    [WindowFact]
    public void RealSdlWindowDisposeIsIdempotentAndRejectsFurtherUse()
    {
        using var sdl = new SilkSdlApi();
        sdl.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2);
        sdl.InitializeVideo();

        var window = sdl.CreateWindow("Bork2d window dispose test", 320, 200, SdlWindowFlags.None);
        window.Dispose();
        window.Dispose();

        Assert.Throws<ObjectDisposedException>(() => window.Size);
        Assert.Throws<ObjectDisposedException>(() => window.SetTitle("after dispose"));
    }
}
