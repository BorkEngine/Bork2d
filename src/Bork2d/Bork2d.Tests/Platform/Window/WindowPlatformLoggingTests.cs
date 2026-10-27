using System.Numerics;
using Bork2d.Runtime.Hosting;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Platform.Window;

/// <summary>
/// The windowed platform logs one structured startup line, one structured stop line, debug
/// command/state transitions, and SDL failures without per-frame logging.
/// </summary>
public sealed class WindowPlatformLoggingTests
{
    [Fact]
    public void StartupLogsModeVersionDriverSizesScaleAndOptions()
    {
        using var factory = CreateFactory(out var provider);
        var logger = factory.CreateLogger(Bork2dLogging.RuntimeCategory);
        var sdl = new FakeSdlApi();
        using var platform = WindowTestSupport.Start(sdl, logger: logger);

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("platform started", StringComparison.Ordinal));

        Assert.Equal(Bork2dLogging.RuntimeCategory, entry.Category);
        Assert.Contains("mode=Windowed", entry.Message, StringComparison.Ordinal);
        Assert.Contains("sdlVersion=2.30.8", entry.Message, StringComparison.Ordinal);
        Assert.Contains("videoDriver=fake", entry.Message, StringComparison.Ordinal);
        Assert.Contains("windowPoints=1280x720", entry.Message, StringComparison.Ordinal);
        Assert.Contains("framebufferPixels=1280x720", entry.Message, StringComparison.Ordinal);
        Assert.Contains("scale=1", entry.Message, StringComparison.Ordinal);
        Assert.Contains("fullscreen=False", entry.Message, StringComparison.Ordinal);
        Assert.Contains("resizable=True", entry.Message, StringComparison.Ordinal);
        Assert.Contains("vsync=True", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandsFocusMinimizeFullscreenIgnoreAndStopAreLogged()
    {
        using var factory = CreateFactory(out var provider);
        var logger = factory.CreateLogger(Bork2dLogging.RuntimeCategory);
        var sdl = new FakeSdlApi();
        var platform = WindowTestSupport.Start(sdl, logger: logger);
        var proxy = WindowTestSupport.Attach(platform);
        var window = Assert.Single(sdl.Windows);

        proxy.Title = "logged";
        WindowTestSupport.WaitUntil(platform, () => proxy.Title == "logged");

        window.SetMinimized(true);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowMinimized));
        WindowTestSupport.WaitUntil(platform, () => proxy.IsMinimized);

        window.SetMinimized(false);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowRestored));
        WindowTestSupport.WaitUntil(platform, () => !proxy.IsMinimized);

        window.SetFocused(true);
        sdl.Enqueue(SdlEvent.Window(SdlEventType.WindowFocusGained));
        WindowTestSupport.WaitUntil(platform, () => platform.State.Focused);

        proxy.Fullscreen = true;
        WindowTestSupport.WaitUntil(platform, () => proxy.Fullscreen);

        proxy.Size = new Vector2(640f, 360f);

        platform.Shutdown();

        Assert.Contains(
            provider.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("command=Title", StringComparison.Ordinal));
        Assert.Contains(
            provider.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("window minimized", StringComparison.Ordinal));
        Assert.Contains(
            provider.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("window restored", StringComparison.Ordinal));
        Assert.Contains(
            provider.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("focus gained", StringComparison.Ordinal));
        Assert.Contains(
            provider.Entries,
            entry => entry.Level == LogLevel.Debug
                && entry.Message.Contains("Size ignored while fullscreen", StringComparison.Ordinal));

        var stop = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Information
                && captured.Message.Contains("platform stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Exit", stop.Message, StringComparison.Ordinal);
        Assert.Contains("finalFramebuffer=", stop.Message, StringComparison.Ordinal);

        platform.Dispose();
    }

    [Fact]
    public void StartupFailureLogsTheSdlError()
    {
        using var factory = CreateFactory(out var provider);
        var logger = factory.CreateLogger(Bork2dLogging.RuntimeCategory);
        var sdl = new FakeSdlApi
        {
            ErrorText = "cannot open display",
            InitializeFailure = "SDL_Init(SDL_INIT_VIDEO) failed: cannot open display",
        };
        var platform = new SdlWindowPlatform(WindowTestSupport.Options(), () => sdl, logger);

        Assert.Throws<Bork2d.Engine.Bork2dException>(platform.Start);
        platform.Dispose();

        var entry = Assert.Single(
            provider.Entries,
            captured => captured.Level == LogLevel.Error
                && captured.Message.Contains("startup", StringComparison.Ordinal));
        Assert.Contains("cannot open display", entry.Message, StringComparison.Ordinal);
    }

    private static ILoggerFactory CreateFactory(out CapturingLoggerProvider provider)
    {
        var capturingProvider = new CapturingLoggerProvider();
        provider = capturingProvider;
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(capturingProvider);
        });
    }
}
