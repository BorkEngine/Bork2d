using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;

namespace Bork2d.Tests.Input;

/// <summary>
/// A deterministic input fixture: a fake-driven windowed platform, the attached
/// <see cref="InputProxy"/>, and bounded waits until the OS thread has applied the sent events.
/// </summary>
/// <remarks>
/// The harness opens no window and uses no timing or sleep; every wait is the platform's bounded
/// input signal with one overall deadline. The fake window starts focused by default because real
/// SDL delivers key, mouse, and text events only to a focused window.
/// </remarks>
internal sealed class InputTestHarness : IDisposable
{
    private InputTestHarness(FakeSdlApi sdl, SdlWindowPlatform platform, InputProxy input)
    {
        Sdl = sdl;
        Platform = platform;
        Input = input;
    }

    /// <summary>Gets the fake SDL seam for input state and text-input assertions.</summary>
    internal FakeSdlApi Sdl { get; }

    /// <summary>Gets the started platform.</summary>
    internal SdlWindowPlatform Platform { get; }

    /// <summary>Gets the attached input proxy.</summary>
    internal InputProxy Input { get; }

    /// <summary>Starts a fake-driven platform and attaches an input proxy.</summary>
    /// <param name="options">The window options; defaults to a 1280x720 window.</param>
    /// <param name="scale">The window-points-to-framebuffer-pixels scale.</param>
    /// <param name="focused">Whether the fake window starts focused.</param>
    /// <returns>The started harness; the caller disposes it.</returns>
    internal static InputTestHarness Start(
        WindowPlatformOptions? options = null,
        float scale = 1f,
        bool focused = true)
    {
        var sdl = new FakeSdlApi { WindowScale = scale, StartFocused = focused };
        var platform = WindowTestSupport.Start(sdl, options);
        var input = WindowTestSupport.AttachInput(platform);
        return new InputTestHarness(sdl, platform, input);
    }

    /// <summary>Enqueues events and waits until the OS thread has applied all of them.</summary>
    /// <param name="events">The events to enqueue in order.</param>
    internal void Send(params SdlEvent[] events)
    {
        var version = Platform.InputVersion;
        foreach (var sdlEvent in events)
        {
            Sdl.Enqueue(sdlEvent);
        }

        WindowTestSupport.WaitForInput(Platform, version, events.Length);
    }

    /// <summary>Freezes the current input into the published snapshot.</summary>
    internal void Freeze() => Input.Freeze();

    /// <inheritdoc/>
    public void Dispose()
    {
        Input.Detach();
        Platform.Dispose();
    }
}
