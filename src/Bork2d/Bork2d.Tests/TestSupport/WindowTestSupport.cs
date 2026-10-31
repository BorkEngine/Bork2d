using System.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Tests.TestSupport;

/// <summary>
/// Shared setup for the deterministic window-platform tests.
/// </summary>
/// <remarks>
/// Waits use the platform's bounded wait overloads and a single overall deadline. A regression that
/// stops publishing state or requesting exit therefore fails the test with a
/// <see cref="TimeoutException"/> instead of hanging the test run; no wait uses a sleep or a poll
/// loop.
/// </remarks>
internal static class WindowTestSupport
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);

    internal static WindowPlatformOptions Options(
        string title = "Bork2d",
        int width = 1280,
        int height = 720,
        bool resizable = true,
        bool fullscreen = false,
        bool vsync = true,
        bool validation = false)
        => new(title, width, height, resizable, fullscreen, vsync, validation);

    internal static SdlWindowPlatform Start(
        FakeSdlApi sdl,
        WindowPlatformOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IVulkanInstanceFactory? instanceFactory = null)
    {
        var platform = new SdlWindowPlatform(
            options ?? Options(),
            () => sdl,
            instanceFactory ?? new FakeVulkanInstanceFactory(),
            loggerFactory ?? NullLoggerFactory.Instance);
        platform.Start();
        return platform;
    }

    internal static WindowProxy Attach(SdlWindowPlatform platform)
    {
        var proxy = new WindowProxy();
        proxy.Attach(platform);
        return proxy;
    }

    internal static InputProxy AttachInput(SdlWindowPlatform platform)
    {
        var proxy = new InputProxy();
        proxy.Attach(platform);
        return proxy;
    }

    internal static void WaitForInput(
        SdlWindowPlatform platform,
        long previousVersion,
        int count = 1)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var version = platform.InputVersion;
            if (version - previousVersion >= count)
            {
                return;
            }

            var remaining = _waitTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero
                || !platform.WaitForInputVersion(version, remaining))
            {
                if (platform.InputVersion - previousVersion >= count)
                {
                    return;
                }

                throw new TimeoutException(
                    $"Timed out after {_waitTimeout.TotalSeconds:0} s waiting for {count} input "
                    + $"event(s). InputVersion={platform.InputVersion}, "
                    + $"requested={previousVersion}+{count}.");
            }
        }
    }

    internal static void WaitUntil(SdlWindowPlatform platform, Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var version = platform.StateVersion;
            if (condition())
            {
                return;
            }

            var remaining = _waitTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero
                || !platform.WaitForStateChange(version, remaining))
            {
                if (condition())
                {
                    return;
                }

                var state = platform.State;
                throw new TimeoutException(
                    $"Timed out after {_waitTimeout.TotalSeconds:0} s waiting for a window-state "
                    + $"condition. Last confirmed state: title='{state.Title}', "
                    + $"framebuffer={state.FramebufferSize}, fullscreen={state.Fullscreen}, "
                    + $"minimized={state.Minimized}, focused={state.Focused}, "
                    + $"alive={state.Alive}.");
            }
        }
    }

    internal static void WaitForExitRequest(SdlWindowPlatform platform)
    {
        if (platform.WaitForExitRequest(_waitTimeout))
        {
            return;
        }

        throw new TimeoutException(
            $"Timed out after {_waitTimeout.TotalSeconds:0} s waiting for the window platform exit "
            + $"request. ExitRequested={platform.ExitRequested}, "
            + $"alive={platform.State.Alive}.");
    }
}
