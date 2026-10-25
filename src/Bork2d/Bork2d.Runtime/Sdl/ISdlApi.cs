namespace Bork2d.Runtime;

/// <summary>
/// The internal seam over the process-level SDL2 calls the windowed platform needs.
/// </summary>
/// <remarks>
/// <para>
/// The production implementation is <see cref="SilkSdlApi"/>; each instance owns its own
/// <c>Sdl</c> instance and native handle, quits SDL, and then frees them. Later work packages
/// depend on this interface only, so deterministic in-memory fakes can drive window creation,
/// window commands, event order, focus/minimize/resize transitions, and input state without a
/// desktop session or the native library.
/// </para>
/// <para>
/// Failures are reported as <see cref="Bork2d.Api.Bork2dException"/> carrying
/// <see cref="GetError"/>. Hints must be set before <see cref="InitializeVideo"/>.
/// </para>
/// <para>
/// No member has thread affinity. The windowed platform creates and uses one instance on its
/// engine-owned OS thread; the seam itself neither asserts nor enforces a thread.
/// </para>
/// </remarks>
internal interface ISdlApi : IDisposable
{
    /// <summary>Gets the SDL library version reported by <c>SDL_GetVersion</c>.</summary>
    SdlVersion Version { get; }

    /// <summary>Gets the active SDL video driver name reported by <c>SDL_GetCurrentVideoDriver</c>.</summary>
    /// <remarks>Returns an empty string when SDL has no current video driver.</remarks>
    string VideoDriver { get; }

    /// <summary>Reads the current <c>SDL_GetError</c> message.</summary>
    /// <returns>The error text, or an empty string when SDL has no error.</returns>
    string GetError();

    /// <summary>Sets an SDL hint before <see cref="InitializeVideo"/>.</summary>
    /// <param name="name">The hint name, for example <see cref="SdlHints.WindowsDpiAwareness"/>.</param>
    /// <param name="value">The hint value.</param>
    /// <returns><see langword="true"/> when SDL accepted the value.</returns>
    bool SetHint(string name, string value);

    /// <summary>Initializes the SDL video subsystem only (<c>SDL_Init(SDL_INIT_VIDEO)</c>).</summary>
    /// <exception cref="Bork2d.Api.Bork2dException">SDL video initialization failed.</exception>
    void InitializeVideo();

    /// <summary>Quits the SDL video subsystem. Idempotent.</summary>
    void Quit();

    /// <summary>Creates one visible window centered on the primary display.</summary>
    /// <param name="title">The initial title.</param>
    /// <param name="width">The initial width in window points.</param>
    /// <param name="height">The initial height in window points.</param>
    /// <param name="flags">The SDL window flags.</param>
    /// <returns>The created window; the caller disposes it to destroy the SDL window.</returns>
    /// <exception cref="Bork2d.Api.Bork2dException">Window creation failed.</exception>
    ISdlWindow CreateWindow(string title, int width, int height, SdlWindowFlags flags);

    /// <summary>Polls one pending event without blocking.</summary>
    /// <param name="sdlEvent">The next event when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the queue is empty.</returns>
    bool TryPollEvent(out SdlEvent sdlEvent);

    /// <summary>Waits for one event.</summary>
    /// <param name="sdlEvent">The next event when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when SDL reports an error.</returns>
    /// <remarks>The call blocks until an event arrives; it is not a pacing wait.</remarks>
    bool TryWaitEvent(out SdlEvent sdlEvent);

    /// <summary>Pushes an event into the SDL queue.</summary>
    /// <param name="sdlEvent">A quit or user event.</param>
    /// <returns><see langword="true"/> when SDL accepted the event.</returns>
    /// <exception cref="NotSupportedException">The event kind cannot be pushed by the seam.</exception>
    bool TryPushEvent(in SdlEvent sdlEvent);

    /// <summary>Registers user event types.</summary>
    /// <param name="count">The number of event types to register.</param>
    /// <returns>The first registered SDL event type, or a negative value on failure.</returns>
    int RegisterEvents(int count);

    /// <summary>Shows or hides the mouse cursor.</summary>
    /// <param name="visible">Whether the cursor is visible.</param>
    /// <exception cref="Bork2d.Api.Bork2dException">SDL failed to change the cursor state.</exception>
    void ShowCursor(bool visible);

    /// <summary>Enables SDL text input.</summary>
    void StartTextInput();

    /// <summary>Disables SDL text input.</summary>
    void StopTextInput();

    /// <summary>Reads the SDL keyboard state.</summary>
    /// <returns>
    /// One byte per scancode, indexed by the physical scancode; empty when SDL has no keyboard
    /// state yet.
    /// </returns>
    ReadOnlySpan<byte> GetKeyboardState();

    /// <summary>Reads the SDL global mouse state.</summary>
    /// <returns>The position in window points and the pressed-button mask.</returns>
    SdlMouseState GetMouseState();
}
