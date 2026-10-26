using System.Numerics;
using Bork2d.Api;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The engine-owned windowed platform: one OS/window thread that owns SDL, the window, the event
/// pump, and the confirmed window state.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> creates the OS thread and blocks until the window exists or startup failed.
/// The thread sets the Windows DPI hints before initializing SDL video, creates one visible
/// high-DPI Vulkan-capable window centered on the primary display, applies the requested
/// framebuffer size, and publishes the first confirmed state. It then pumps SDL events until
/// <see cref="Shutdown"/> stops it. <see cref="Shutdown"/> wakes the event wait, joins the thread,
/// and lets the thread destroy the window before quitting and disposing SDL.
/// </para>
/// <para>
/// Window commands arrive through <see cref="WindowCommandQueue"/>. Setters may run on any thread,
/// never block on SDL, and may coalesce; the OS thread drains the queue before waiting and again
/// after each event, so a command cannot be lost. Getters read the immutable
/// <see cref="WindowState"/> snapshot without blocking and may lag one pump.
/// </para>
/// <para>
/// A window close or <c>SDL_QUIT</c> requests a graceful exit but does not destroy the window;
/// the host stops its loop and calls <see cref="Shutdown"/> after game teardown. Unrecoverable SDL
/// errors are stored in <see cref="FatalException"/> and also request an exit.
/// </para>
/// <para>
/// No lock is held across an SDL call, a game callback, or the event wait. The only blocking waits
/// are SDL's event wait, the startup/exit signals, and the thread join.
/// </para>
/// </remarks>
internal sealed class SdlWindowPlatform : IDisposable
{
    private const string ThreadName = "Bork2d.Window";
    private const float ScaleTolerance = 0.001f;

    private readonly WindowPlatformOptions _options;
    private readonly Func<ISdlApi> _sdlApiFactory;
    private readonly ILogger _logger;
    private readonly WindowCommandQueue _commands;
    private readonly InputAccumulator _input = new();
    private readonly ManualResetEventSlim _startedSignal = new(initialState: false);
    private readonly ManualResetEventSlim _exitRequestedSignal = new(initialState: false);
    private readonly ManualResetEventSlim _stateChangedSignal = new(initialState: false);
    private readonly ManualResetEventSlim _inputChangedSignal = new(initialState: false);

    private WindowState _state;
    private int _stateVersion;
    private long _inputVersion;
    private bool _textInputActive;
    private Thread? _thread;
    private ISdlApi? _sdl;
    private int _userEventType = -1;
    private int _stopReason;
    private int _shutdownState;
    private Exception? _startFailure;
    private Bork2dException? _fatalException;
    private Vector2? _restoreSize;
    private volatile bool _stop;
    private bool _exitRequested;
    private volatile bool _startedSuccessfully;

    /// <summary>Initializes a new instance of the <see cref="SdlWindowPlatform"/> class.</summary>
    /// <param name="options">The builder-derived window options.</param>
    /// <param name="sdlApiFactory">
    /// Creates the SDL seam on the OS thread. Tests supply a fake; production supplies
    /// <see cref="SilkSdlApi"/>.
    /// </param>
    /// <param name="logger">The <c>Bork2d.Runtime</c> logger.</param>
    internal SdlWindowPlatform(
        WindowPlatformOptions options,
        Func<ISdlApi> sdlApiFactory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sdlApiFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _sdlApiFactory = sdlApiFactory;
        _logger = logger;
        _commands = new WindowCommandQueue(
            options.Title,
            options.Width,
            options.Height,
            options.Fullscreen,
            initialCursorVisible: true);
        _state = new WindowState(
            options.Title,
            default,
            default,
            scale: 1f,
            options.Fullscreen,
            cursorVisible: true,
            minimized: false,
            focused: false,
            visible: false,
            alive: false);
    }

    /// <summary>Gets the latest confirmed window state snapshot.</summary>
    internal WindowState State => Volatile.Read(ref _state);

    /// <summary>Gets a counter that increments after every confirmed-state publication.</summary>
    /// <remarks>
    /// <see cref="WaitForStateChange"/> uses it to observe a publication without polling or a
    /// timeout; it is not part of the runtime contract.
    /// </remarks>
    internal int StateVersion => Volatile.Read(ref _stateVersion);

    /// <summary>Gets a counter that increments after every applied input event.</summary>
    /// <remarks>
    /// <see cref="WaitForInputVersion"/> uses it to observe input application without polling or a
    /// timeout; like <see cref="StateVersion"/> it is diagnostic and not part of the runtime contract.
    /// </remarks>
    internal long InputVersion => Volatile.Read(ref _inputVersion);

    /// <summary>Gets a value indicating whether a graceful exit (close, QUIT, or fatal) was requested.</summary>
    internal bool ExitRequested => Volatile.Read(ref _exitRequested);

    /// <summary>Gets the unrecoverable SDL error, or <see langword="null"/> when none occurred.</summary>
    internal Bork2dException? FatalException => Volatile.Read(ref _fatalException);

    /// <summary>Gets why the platform stopped.</summary>
    internal WindowStopReason StopReason => (WindowStopReason)Volatile.Read(ref _stopReason);

    /// <summary>Gets the <c>Bork2d.Runtime</c> logger used by the platform and its window proxy.</summary>
    internal ILogger Logger => _logger;

    /// <summary>Starts the OS thread and blocks until the window is created or startup failed.</summary>
    /// <exception cref="Bork2dException">
    /// SDL initialization or window creation failed. The message contains the SDL error text; the
    /// OS thread is joined and SDL is cleaned up before the exception is thrown.
    /// </exception>
    internal void Start()
    {
        if (Volatile.Read(ref _shutdownState) != 0)
        {
            throw new InvalidOperationException("The windowed platform has already been shut down.");
        }

        if (_thread is not null)
        {
            throw new InvalidOperationException("The windowed platform has already been started.");
        }

        var thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = ThreadName,
        };
        _thread = thread;
        thread.Start();
        _startedSignal.Wait();

        if (_startFailure is { } failure)
        {
            thread.Join();
            Interlocked.Exchange(ref _shutdownState, 1);
            if (failure is Bork2dException bork2dException)
            {
                throw bork2dException;
            }

            throw new Bork2dException(
                "The windowed platform failed to start; SDL did not create the window.",
                failure);
        }
    }

    /// <summary>Requests a graceful exit with the <see cref="WindowStopReason.Exit"/> reason.</summary>
    internal void RequestExit() => RequestExit(WindowStopReason.Exit);

    /// <summary>Blocks until a confirmed-state publication newer than <paramref name="previousVersion"/>.</summary>
    /// <param name="previousVersion">The version returned by <see cref="StateVersion"/> before the change.</param>
    internal void WaitForStateChange(int previousVersion)
    {
        if (Volatile.Read(ref _stateVersion) != previousVersion)
        {
            return;
        }

        // Reset is safe because publications bump the version before signalling: a version change
        // observed after the reset returns immediately instead of waiting for a lost signal.
        _stateChangedSignal.Reset();
        if (Volatile.Read(ref _stateVersion) != previousVersion)
        {
            return;
        }

        _stateChangedSignal.Wait();
    }

    /// <summary>Waits for a confirmed-state publication for at most a bounded interval.</summary>
    /// <param name="previousVersion">The version returned by <see cref="StateVersion"/> before the change.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>
    /// <see langword="true"/> when the state version changed; <see langword="false"/> when the
    /// timeout elapsed first.
    /// </returns>
    /// <remarks>
    /// This bounded overload exists for deterministic tests and diagnostics so a missing
    /// publication fails instead of hanging the caller. Production code uses the no-timeout
    /// <see cref="WaitForStateChange(int)"/> path, and neither overload sleeps or polls.
    /// </remarks>
    internal bool WaitForStateChange(int previousVersion, TimeSpan timeout)
    {
        if (Volatile.Read(ref _stateVersion) != previousVersion)
        {
            return true;
        }

        _stateChangedSignal.Reset();
        if (Volatile.Read(ref _stateVersion) != previousVersion)
        {
            return true;
        }

        return _stateChangedSignal.Wait(timeout);
    }

    /// <summary>Waits for an applied-input publication for at most a bounded interval.</summary>
    /// <param name="previousVersion">The <see cref="InputVersion"/> observed before the events.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>
    /// <see langword="true"/> when the input version changed; <see langword="false"/> when the
    /// timeout elapsed first.
    /// </returns>
    /// <remarks>
    /// This bounded overload exists for deterministic tests so a missing input application fails
    /// instead of hanging; production code never waits on input. It does not sleep or poll.
    /// </remarks>
    internal bool WaitForInputVersion(long previousVersion, TimeSpan timeout)
    {
        if (Volatile.Read(ref _inputVersion) != previousVersion)
        {
            return true;
        }

        _inputChangedSignal.Reset();
        if (Volatile.Read(ref _inputVersion) != previousVersion)
        {
            return true;
        }

        return _inputChangedSignal.Wait(timeout);
    }

    /// <summary>Freezes the platform input into <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The preallocated snapshot instance the input proxy publishes.</param>
    /// <remarks>
    /// Called on the tick thread once per tick. It copies the accumulated state under the input lock,
    /// converts the mouse position from window points to framebuffer pixels with the current confirmed
    /// scale, and clears pending edges and wheel state. It performs no SDL call and allocates nothing
    /// when no text is pending.
    /// </remarks>
    internal void FreezeInput(InputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var scale = State.Scale;
        _input.Freeze(snapshot, scale > 0f ? scale : 1f);
    }

    /// <summary>Blocks until <see cref="ExitRequested"/> becomes <see langword="true"/>.</summary>
    internal void WaitForExitRequest() => _exitRequestedSignal.Wait();

    /// <summary>Waits for the exit request for at most a bounded interval.</summary>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>
    /// <see langword="true"/> when the exit request was signalled; <see langword="false"/> when the
    /// timeout elapsed first.
    /// </returns>
    /// <remarks>
    /// This bounded overload exists for deterministic tests so a missing exit request fails instead
    /// of hanging the caller. It does not change the platform's no-timeout shutdown semantics.
    /// </remarks>
    internal bool WaitForExitRequest(TimeSpan timeout) => _exitRequestedSignal.Wait(timeout);

    /// <summary>Signals stop, wakes the event wait, and joins the OS thread.</summary>
    /// <remarks>
    /// Idempotent. The OS thread destroys the window before quitting and disposing SDL; this method
    /// returns only after that cleanup and the thread exit. It never uses a timeout or a sleep.
    /// </remarks>
    internal void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownState, 1) != 0)
        {
            return;
        }

        var thread = _thread;
        if (thread is null)
        {
            return;
        }

        _stop = true;
        RequestExit(WindowStopReason.Exit);
        Wake();
        thread.Join();
    }

    /// <summary>Stops the platform. Idempotent.</summary>
    public void Dispose()
    {
        Shutdown();
        _startedSignal.Dispose();
        _exitRequestedSignal.Dispose();
        _stateChangedSignal.Dispose();
        _inputChangedSignal.Dispose();
    }

    /// <summary>Enqueues a title change and wakes the OS thread.</summary>
    /// <param name="title">The non-null requested title.</param>
    /// <returns><see langword="true"/> when the request was queued or coalesced.</returns>
    internal bool SetTitle(string title)
    {
        var queued = _commands.TrySetTitle(title);
        if (queued)
        {
            Wake();
        }

        return queued;
    }

    /// <summary>Enqueues a framebuffer-size request and wakes the OS thread.</summary>
    /// <param name="width">The requested framebuffer width in physical pixels.</param>
    /// <param name="height">The requested framebuffer height in physical pixels.</param>
    /// <returns><see langword="true"/> when the request was queued or coalesced.</returns>
    internal bool SetSize(float width, float height)
    {
        var queued = _commands.TrySetSize(width, height);
        if (queued)
        {
            Wake();
        }

        return queued;
    }

    /// <summary>Enqueues a fullscreen change and wakes the OS thread.</summary>
    /// <param name="fullscreen">The requested fullscreen state.</param>
    /// <returns><see langword="true"/> when the request was queued or coalesced.</returns>
    internal bool SetFullscreen(bool fullscreen)
    {
        var queued = _commands.TrySetFullscreen(fullscreen);
        if (queued)
        {
            Wake();
        }

        return queued;
    }

    /// <summary>Enqueues a cursor-visibility change and wakes the OS thread.</summary>
    /// <param name="visible">The requested cursor visibility.</param>
    /// <returns><see langword="true"/> when the request was queued or coalesced.</returns>
    internal bool SetCursorVisible(bool visible)
    {
        var queued = _commands.TrySetCursorVisible(visible);
        if (queued)
        {
            Wake();
        }

        return queued;
    }

    private void ThreadMain()
    {
        ISdlApi? sdl = null;
        ISdlWindow? window = null;
        try
        {
            sdl = _sdlApiFactory();
            Volatile.Write(ref _sdl, sdl);

            // DPI hints must precede SDL_Init: SDL reads the awareness and scaling mode while it
            // initializes the video subsystem. Awareness makes window points logical coordinates
            // (D-M2-10) while SDL_GetWindowSizeInPixels stays physical.
            sdl.SetHint(SdlHints.WindowsDpiAwareness, SdlHints.PerMonitorV2);
            sdl.SetHint(SdlHints.WindowsDpiScaling, SdlHints.DpiScalingEnabled);
            sdl.InitializeVideo();

            window = sdl.CreateWindow(
                _options.Title,
                _options.Width,
                _options.Height,
                CreateWindowFlags());

            var userEventType = sdl.RegisterEvents(1);
            if (userEventType < 0)
            {
                throw new Bork2dException("SDL_RegisterEvents failed: " + sdl.GetError());
            }

            Volatile.Write(ref _userEventType, userEventType);

            ApplyInitialFramebufferSize(window);
            var flags = window.Flags;
            var focused = (flags & SdlWindowFlags.InputFocus) != 0;
            _input.SetFocused(focused);
            if (focused)
            {
                StartTextInput(sdl);
            }

            Publish(
                BuildState(
                    window,
                    minimized: (flags & SdlWindowFlags.Minimized) != 0,
                    focused: focused,
                    alive: true));
            LogStarted(sdl, State);

            _startedSuccessfully = true;
            _startedSignal.Set();
            Pump(sdl, window);
        }
        catch (Exception exception)
        {
            if (!_startedSuccessfully)
            {
                _startFailure = CreateStartFailure(exception);
                LogStartFailure(_startFailure);
            }
            else
            {
                RecordFatal(exception, "window thread");
            }
        }
        finally
        {
            Cleanup(window, sdl);
            _startedSignal.Set();
        }
    }

    private void Pump(ISdlApi sdl, ISdlWindow window)
    {
        while (!_stop)
        {
            // Drain events first: a resize or focus event can wake the wait and the queue may also
            // contain commands from the same interval.
            while (sdl.TryPollEvent(out var polledEvent))
            {
                HandleEvent(sdl, window, polledEvent);
                if (_stop)
                {
                    break;
                }
            }

            ApplyCommands(sdl, window);
            if (_stop)
            {
                break;
            }

            if (!sdl.TryWaitEvent(out var waitedEvent))
            {
                RecordFatal(
                    new Bork2dException("SDL_WaitEvent failed: " + sdl.GetError()),
                    "SDL_WaitEvent");
                break;
            }

            // The loop re-drains events and commands before blocking again, so a command queued
            // between the drain and the wait is applied after this wake-up instead of being lost.
            HandleEvent(sdl, window, waitedEvent);
        }
    }

    private void HandleEvent(ISdlApi sdl, ISdlWindow window, in SdlEvent sdlEvent)
    {
        // Input is applied first, exactly once per event, so focus transitions are recorded in event
        // order before the window state reflects them. A close/QUIT event has no input effect and is
        // still consumed by the window-state switch below.
        if (HandleInputEvent(sdl, sdlEvent))
        {
            Interlocked.Increment(ref _inputVersion);
            _inputChangedSignal.Set();
        }

        switch (sdlEvent.Type)
        {
            case SdlEventType.Quit:
            case SdlEventType.WindowClose:
                // The close button requests a graceful exit; the window stays alive for Teardown
                // and is destroyed by Shutdown.
                RequestExit(WindowStopReason.Close);
                break;

            case SdlEventType.WindowMinimized:
                Publish(BuildState(window, minimized: true));
                _logger.LogDebug("Bork2d window minimized");
                break;

            case SdlEventType.WindowMaximized:
                Publish(BuildState(window, minimized: false));
                _logger.LogDebug("Bork2d window maximized");
                break;

            case SdlEventType.WindowRestored:
                RestoreWindow(window);
                _logger.LogDebug("Bork2d window restored");
                break;

            case SdlEventType.WindowResized:
            case SdlEventType.WindowSizeChanged:
                HandleSizeChanged(window, sdlEvent);
                break;

            case SdlEventType.WindowFocusGained:
                Publish(BuildState(window, focused: true));
                _logger.LogDebug("Bork2d window focus gained");
                break;

            case SdlEventType.WindowFocusLost:
                Publish(BuildState(window, focused: false));
                _logger.LogDebug("Bork2d window focus lost");
                break;

            default:
                // User wake-ups and unconsumed events need no state change.
                break;
        }
    }

    /// <summary>Applies one event to the input accumulator.</summary>
    /// <param name="sdl">The SDL seam, used only for focus text-input and re-seed reads.</param>
    /// <param name="sdlEvent">The event to apply.</param>
    /// <returns><see langword="true"/> when the event was an input event.</returns>
    /// <remarks>
    /// No input lock is held across an SDL call: the re-seed reads happen before the accumulator is
    /// updated, and text-input enablement happens after it. A failure propagates to the pump, which
    /// records it as a fatal platform error.
    /// </remarks>
    private bool HandleInputEvent(ISdlApi sdl, in SdlEvent sdlEvent)
    {
        switch (sdlEvent.Type)
        {
            case SdlEventType.KeyDown:
                _input.KeyDown(sdlEvent.Scancode, sdlEvent.IsRepeat);
                return true;

            case SdlEventType.KeyUp:
                _input.KeyUp(sdlEvent.Scancode);
                return true;

            case SdlEventType.MouseMotion:
                _input.MouseMotion(sdlEvent.MouseX, sdlEvent.MouseY);
                return true;

            case SdlEventType.MouseButtonDown:
                _input.MouseButtonDown(sdlEvent.ButtonIndex);
                return true;

            case SdlEventType.MouseButtonUp:
                _input.MouseButtonUp(sdlEvent.ButtonIndex);
                return true;

            case SdlEventType.MouseWheel:
                _input.MouseWheel(sdlEvent.WheelY, sdlEvent.IsWheelFlipped);
                return true;

            case SdlEventType.TextInput:
                _input.Text(sdlEvent.Text);
                return true;

            case SdlEventType.WindowFocusLost:
                _input.FocusLost();
                StopTextInput(sdl);
                return true;

            case SdlEventType.WindowFocusGained:
                // Read the SDL state before taking the input lock; both calls are thread-affine to
                // the OS thread that owns SDL.
                var keyboardState = sdl.GetKeyboardState();
                var mouseState = sdl.GetMouseState();
                _input.FocusGained(keyboardState, mouseState);
                StartTextInput(sdl);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Enables SDL text input once, while focused.</summary>
    /// <param name="sdl">The SDL seam.</param>
    private void StartTextInput(ISdlApi sdl)
    {
        if (_textInputActive)
        {
            return;
        }

        sdl.StartTextInput();
        _textInputActive = true;
    }

    /// <summary>Disables SDL text input when it is active.</summary>
    /// <param name="sdl">The SDL seam.</param>
    private void StopTextInput(ISdlApi sdl)
    {
        if (!_textInputActive)
        {
            return;
        }

        sdl.StopTextInput();
        _textInputActive = false;
    }

    private void HandleSizeChanged(ISdlWindow window, in SdlEvent sdlEvent)
    {
        if (State.Minimized)
        {
            _logger.LogDebug(
                "Bork2d window size change ignored while minimized: points={Width}x{Height}",
                sdlEvent.Width,
                sdlEvent.Height);
            return;
        }

        if (sdlEvent.Width <= 0 || sdlEvent.Height <= 0)
        {
            // Keep the last confirmed non-zero framebuffer size.
            return;
        }

        var previous = State;
        var confirmed = BuildState(window);
        Publish(confirmed);

        if (confirmed.Fullscreen != previous.Fullscreen)
        {
            _logger.LogDebug(
                "Bork2d window fullscreen changed: fullscreen={Fullscreen}",
                confirmed.Fullscreen);
        }
    }

    private void RestoreWindow(ISdlWindow window)
    {
        if (!State.Fullscreen && _restoreSize is { } restore)
        {
            _restoreSize = null;
            var scale = State.Scale > 0f ? State.Scale : 1f;
            window.SetSize(
                ToWindowPoints(restore.X, scale),
                ToWindowPoints(restore.Y, scale));
        }

        Publish(BuildState(window, minimized: false));
    }

    private void ApplyCommands(ISdlApi sdl, ISdlWindow window)
    {
        foreach (var command in _commands.Drain())
        {
            try
            {
                ApplyCommand(sdl, window, command);
            }
            catch (Exception exception)
            {
                RecordFatal(exception, "window command " + command.Kind);
                return;
            }
        }
    }

    private void ApplyCommand(ISdlApi sdl, ISdlWindow window, in WindowCommand command)
    {
        switch (command.Kind)
        {
            case WindowCommandKind.Title:
                var title = command.Title ?? string.Empty;
                window.SetTitle(title);
                Publish(BuildState(window, title: title));
                _logger.LogDebug(
                    "Bork2d window command applied: command={Command}, confirmed={Confirmed}",
                    nameof(WindowCommandKind.Title),
                    title);
                break;

            case WindowCommandKind.Size:
                ApplySizeRequest(window, command.Width, command.Height);
                break;

            case WindowCommandKind.Fullscreen:
                window.SetFullscreen(command.Flag);
                var fullscreenState = BuildState(window);
                Publish(fullscreenState);
                _logger.LogDebug(
                    "Bork2d window command applied: command={Command}, confirmed={Confirmed}",
                    nameof(WindowCommandKind.Fullscreen),
                    command.Flag);
                if (!fullscreenState.Fullscreen && _restoreSize is { } restore)
                {
                    _restoreSize = null;
                    ApplySizeRequest(window, restore.X, restore.Y);
                }

                break;

            case WindowCommandKind.CursorVisible:
                sdl.ShowCursor(command.Flag);
                Publish(BuildState(window, cursorVisible: command.Flag));
                _logger.LogDebug(
                    "Bork2d window command applied: command={Command}, confirmed={Confirmed}",
                    nameof(WindowCommandKind.CursorVisible),
                    command.Flag);
                break;

            default:
                break;
        }
    }

    private void ApplySizeRequest(ISdlWindow window, float width, float height)
    {
        var state = State;
        if (state.Fullscreen)
        {
            _logger.LogDebug(
                "Bork2d window Size ignored while fullscreen: requested={Width}x{Height}",
                width,
                height);
            return;
        }

        if (state.Minimized)
        {
            // The getter keeps the last confirmed size; the request becomes the restored size.
            _restoreSize = new Vector2(width, height);
            _logger.LogDebug(
                "Bork2d window Size deferred while minimized: requested={Width}x{Height}",
                width,
                height);
            return;
        }

        var scale = state.Scale > 0f ? state.Scale : 1f;
        window.SetSize(
            ToWindowPoints(width, scale),
            ToWindowPoints(height, scale));
        var confirmed = BuildState(window);
        Publish(confirmed);
        _logger.LogDebug(
            "Bork2d window command applied: command={Command}, confirmed={Confirmed}",
            nameof(WindowCommandKind.Size),
            $"{confirmed.FramebufferSize.Width}x{confirmed.FramebufferSize.Height}");
    }

    private void ApplyInitialFramebufferSize(ISdlWindow window)
    {
        if (_options.Fullscreen)
        {
            // The initial size is ignored until the window leaves fullscreen; SDL owns the
            // fullscreen framebuffer size.
            return;
        }

        var points = window.Size;
        var pixels = window.SizeInPixels;
        if (points.IsEmpty || pixels.IsEmpty)
        {
            return;
        }

        var scale = ComputeScale(points, pixels);
        if (pixels.Width == _options.Width && pixels.Height == _options.Height)
        {
            return;
        }

        var targetWidth = ToWindowPoints(_options.Width, scale);
        var targetHeight = ToWindowPoints(_options.Height, scale);
        _logger.LogDebug(
            "Bork2d window initial size adjustment: measuredPoints={MeasuredWidth}x{MeasuredHeight}, measuredFramebuffer={MeasuredPixelWidth}x{MeasuredPixelHeight}, scale={Scale}, requestedFramebuffer={RequestedWidth}x{RequestedHeight}, targetPoints={TargetWidth}x{TargetHeight}",
            points.Width,
            points.Height,
            pixels.Width,
            pixels.Height,
            scale,
            _options.Width,
            _options.Height,
            targetWidth,
            targetHeight);
        window.SetSize(targetWidth, targetHeight);
    }

    private WindowState BuildState(
        ISdlWindow window,
        string? title = null,
        bool? minimized = null,
        bool? focused = null,
        bool? cursorVisible = null,
        bool? alive = null)
    {
        var previous = State;
        var minimizedState = minimized ?? previous.Minimized;
        var flags = window.Flags;
        var fullscreen = (flags & (SdlWindowFlags.Fullscreen | SdlWindowFlags.FullscreenDesktop)) != 0;
        var visible = (flags & SdlWindowFlags.Shown) != 0 && (flags & SdlWindowFlags.Hidden) == 0;

        SdlWindowSize points;
        SdlWindowSize pixels;
        float scale;
        if (minimizedState)
        {
            // D-M2-11: keep the last confirmed non-zero framebuffer size while minimized.
            points = previous.WindowPoints;
            pixels = previous.FramebufferSize;
            scale = previous.Scale;
        }
        else
        {
            points = window.Size;
            pixels = window.SizeInPixels;
            if (points.IsEmpty || pixels.IsEmpty)
            {
                points = previous.WindowPoints;
                pixels = previous.FramebufferSize;
                scale = previous.Scale;
            }
            else
            {
                scale = ComputeScale(points, pixels);
                if (!(scale > 0f))
                {
                    scale = previous.Scale;
                }
            }
        }

        return new WindowState(
            title ?? previous.Title,
            points,
            pixels,
            scale,
            fullscreen,
            cursorVisible ?? previous.CursorVisible,
            minimizedState,
            focused ?? previous.Focused,
            visible,
            alive ?? previous.Alive);
    }

    private void Publish(WindowState state)
    {
        var previous = State;
        Volatile.Write(ref _state, state);

        if (!previous.FramebufferSize.IsEmpty
            && MathF.Abs(state.Scale - previous.Scale) > ScaleTolerance)
        {
            _logger.LogDebug(
                "Bork2d window DPI scale changed: scale={Scale}, windowPoints={Width}x{Height}, framebuffer={FramebufferWidth}x{FramebufferHeight}",
                state.Scale,
                state.WindowPoints.Width,
                state.WindowPoints.Height,
                state.FramebufferSize.Width,
                state.FramebufferSize.Height);
        }

        Interlocked.Increment(ref _stateVersion);
        _stateChangedSignal.Set();
    }

    private void RecordFatal(Exception exception, string operation)
    {
        var sdlError = TryGetSdlError();
        var fatal = exception as Bork2dException
            ?? new Bork2dException(
                sdlError.Length == 0
                    ? $"SDL window operation '{operation}' failed: {exception.Message}"
                    : $"SDL window operation '{operation}' failed: {exception.Message} "
                        + $"(SDL error: {sdlError})",
                exception);
        if (Volatile.Read(ref _fatalException) is null)
        {
            Volatile.Write(ref _fatalException, fatal);
        }

        _logger.LogError(
            fatal,
            "SDL failure: operation={Operation}, error={SdlError}",
            operation,
            sdlError.Length == 0 ? fatal.Message : sdlError);
        _stop = true;
        RequestExit(WindowStopReason.Error);
    }

    private void RequestExit(WindowStopReason reason)
    {
        SetStopReason(reason);
        Volatile.Write(ref _exitRequested, true);
        _exitRequestedSignal.Set();
    }

    private void SetStopReason(WindowStopReason reason)
    {
        while (true)
        {
            var current = (WindowStopReason)Volatile.Read(ref _stopReason);
            if (!IsStronger(reason, current))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)current)
                == (int)current)
            {
                return;
            }
        }
    }

    private static bool IsStronger(WindowStopReason candidate, WindowStopReason current)
        => candidate switch
        {
            WindowStopReason.Error => current != WindowStopReason.Error,
            WindowStopReason.Close => current is WindowStopReason.None or WindowStopReason.Exit,
            WindowStopReason.Exit => current == WindowStopReason.None,
            _ => false,
        };

    private void Wake()
    {
        var sdl = Volatile.Read(ref _sdl);
        var eventType = Volatile.Read(ref _userEventType);
        if (sdl is null || eventType < 0)
        {
            return;
        }

        try
        {
            sdl.TryPushEvent(SdlEvent.User(eventType));
        }
        catch (ObjectDisposedException)
        {
            // The OS thread already finished cleanup; there is no waiter left to wake.
        }
    }

    private void Cleanup(ISdlWindow? window, ISdlApi? sdl)
    {
        if (_textInputActive && sdl is not null)
        {
            try
            {
                sdl.StopTextInput();
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "SDL failure while stopping text input: {SdlError}",
                    TryGetSdlError());
            }
            finally
            {
                _textInputActive = false;
            }
        }

        var finalState = State;
        try
        {
            window?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "SDL failure while destroying the window: {SdlError}",
                TryGetSdlError());
        }

        if (finalState.Alive)
        {
            Publish(finalState.WithAlive(alive: false));
        }

        try
        {
            sdl?.Quit();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SDL failure while quitting video: {SdlError}", TryGetSdlError());
        }

        try
        {
            sdl?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SDL failure while disposing the adapter: {SdlError}", TryGetSdlError());
        }

        if (_startedSuccessfully)
        {
            var reason = StopReason == WindowStopReason.None ? WindowStopReason.Exit : StopReason;
            _logger.LogInformation(
                "Bork2d windowed platform stopped: reason={Reason}, finalFramebuffer={Width}x{Height}",
                reason,
                finalState.FramebufferSize.Width,
                finalState.FramebufferSize.Height);
        }
    }

    private void LogStarted(ISdlApi sdl, WindowState state)
    {
        _logger.LogInformation(
            "Bork2d windowed platform started: mode={Mode}, sdlVersion={SdlVersion}, videoDriver={VideoDriver}, windowPoints={WindowWidth}x{WindowHeight}, framebufferPixels={FramebufferWidth}x{FramebufferHeight}, scale={Scale}, fullscreen={Fullscreen}, resizable={Resizable}, vsync={VSync}",
            "Windowed",
            sdl.Version.ToString(),
            sdl.VideoDriver,
            state.WindowPoints.Width,
            state.WindowPoints.Height,
            state.FramebufferSize.Width,
            state.FramebufferSize.Height,
            state.Scale,
            state.Fullscreen,
            _options.Resizable,
            _options.VSync);
    }

    private void LogStartFailure(Exception exception)
    {
        var sdlError = TryGetSdlError();
        _logger.LogError(
            exception,
            "SDL failure during windowed platform startup: operation={Operation}, error={SdlError}",
            "startup",
            sdlError.Length == 0 ? exception.Message : sdlError);
    }

    private Bork2dException CreateStartFailure(Exception exception)
    {
        if (exception is Bork2dException bork2dException)
        {
            return bork2dException;
        }

        var sdlError = TryGetSdlError();
        var message = sdlError.Length == 0
            ? $"The windowed platform failed to start: {exception.Message}"
            : $"The windowed platform failed to start: {exception.Message} "
                + $"(SDL error: {sdlError})";
        return new Bork2dException(message, exception);
    }

    private string TryGetSdlError()
    {
        var sdl = Volatile.Read(ref _sdl);
        if (sdl is null)
        {
            return string.Empty;
        }

        try
        {
            return sdl.GetError();
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private SdlWindowFlags CreateWindowFlags()
    {
        var flags = SdlWindowFlags.Vulkan | SdlWindowFlags.AllowHighDpi;
        if (_options.Resizable)
        {
            flags |= SdlWindowFlags.Resizable;
        }

        if (_options.Fullscreen)
        {
            flags |= SdlWindowFlags.FullscreenDesktop;
        }

        return flags;
    }

    private static float ComputeScale(SdlWindowSize points, SdlWindowSize pixels)
    {
        if (points.Width > 0 && pixels.Width > 0)
        {
            return (float)pixels.Width / points.Width;
        }

        if (points.Height > 0 && pixels.Height > 0)
        {
            return (float)pixels.Height / points.Height;
        }

        return 1f;
    }

    private static int ToWindowPoints(float framebufferPixels, float scale)
    {
        var points = framebufferPixels / (scale > 0f ? scale : 1f);
        if (points >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)MathF.Round(points));
    }
}
