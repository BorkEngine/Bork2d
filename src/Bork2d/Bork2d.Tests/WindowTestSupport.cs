using Bork2d.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Tests;

/// <summary>
/// Shared setup for the deterministic window-platform tests.
/// </summary>
internal static class WindowTestSupport
{
    internal static WindowPlatformOptions Options(
        string title = "Bork2d",
        int width = 1280,
        int height = 720,
        bool resizable = true,
        bool fullscreen = false,
        bool vsync = true)
        => new(title, width, height, resizable, fullscreen, vsync);

    internal static SdlWindowPlatform Start(
        FakeSdlApi sdl,
        WindowPlatformOptions? options = null,
        ILogger? logger = null)
    {
        var platform = new SdlWindowPlatform(
            options ?? Options(),
            () => sdl,
            logger ?? NullLogger.Instance);
        platform.Start();
        return platform;
    }

    internal static WindowProxy Attach(SdlWindowPlatform platform)
    {
        var proxy = new WindowProxy();
        proxy.Attach(platform);
        return proxy;
    }

    internal static void WaitUntil(SdlWindowPlatform platform, Func<bool> condition)
    {
        while (true)
        {
            var version = platform.StateVersion;
            if (condition())
            {
                return;
            }

            platform.WaitForStateChange(version);
        }
    }
}
