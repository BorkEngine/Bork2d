using Bork2d.Api;
using Bork2d.Runtime;

namespace Bork2d.Tests;

/// <summary>
/// An in-memory <see cref="ISdlApi"/> that drives window creation, commands, event order, focus,
/// minimize/resize transitions, and input state without loading SDL.
/// </summary>
/// <remarks>
/// <para>
/// The fake is the deterministic platform for windowed runtime tests. It never touches the native
/// library or a desktop session. Events are queued in call order; <see cref="TryPollEvent"/> drains
/// the queue in that order and <see cref="TryWaitEvent"/> blocks until an event arrives, mirroring
/// the SDL wait contract without a timeout. <see cref="TryPushEvent"/> appends to the queue and
/// wakes the waiter, so tests can simulate close, wake-up, and window events.
/// </para>
/// <para>
/// <see cref="Calls"/> records the seam calls in order, which lets tests prove hint ordering and
/// shutdown ordering. All queue and call-log access is serialized so the fake can be used from the
/// platform's OS thread and the test thread.
/// </para>
/// </remarks>
internal sealed class FakeSdlApi : ISdlApi
{
    private readonly Lock _gate = new();
    private readonly Queue<SdlEvent> _events = new();
    private readonly SemaphoreSlim _eventAvailable = new(initialCount: 0);
    private readonly List<string> _calls = [];
    private readonly byte[] _keyboardState = new byte[512];
    private int _nextUserEventType = 0x8000;
    private volatile bool _videoInitialized;
    private volatile bool _disposed;

    /// <summary>Gets the windows created through <see cref="CreateWindow"/> in creation order.</summary>
    internal List<FakeSdlWindow> Windows { get; } = [];

    /// <summary>Gets or sets the hints set through <see cref="SetHint"/> in call order.</summary>
    internal List<(string Name, string Value)> Hints { get; } = [];

    /// <summary>Gets a snapshot of the seam calls in call order.</summary>
    internal IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Gets the managed thread id that called <see cref="InitializeVideo"/>.</summary>
    internal int InitializeVideoThreadId { get; private set; }

    /// <summary>Gets the managed thread id that called <see cref="CreateWindow"/>.</summary>
    internal int CreateWindowThreadId { get; private set; }

    /// <summary>Gets the managed thread id that disposed the adapter.</summary>
    internal int DisposeThreadId { get; private set; }

    /// <summary>
    /// Gets or sets a callback invoked after a seam call is recorded, outside the call-log lock.
    /// </summary>
    /// <remarks>
    /// Tests use this to wait deterministically until the OS thread has applied a window command
    /// (for example a title change ordered after an input event) instead of polling or sleeping.
    /// </remarks>
    internal Action<string>? CallRecorded { get; set; }

    /// <summary>Gets or sets a message that makes the event poll/wait fail with a fatal SDL error.</summary>
    internal string? EventPollFailure { get; set; }

    /// <summary>Gets or sets the version reported by <see cref="Version"/>.</summary>
    public SdlVersion Version { get; set; } = new(2, 30, 8);

    /// <summary>Gets or sets the video driver reported by <see cref="VideoDriver"/>.</summary>
    internal string VideoDriverName { get; set; } = "fake";

    /// <summary>Gets or sets the <c>SDL_GetError</c> text returned by <see cref="GetError"/>.</summary>
    internal string ErrorText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="InitializeVideo"/> succeeded.</summary>
    internal bool VideoInitialized => _videoInitialized;

    /// <summary>Gets a value indicating whether <see cref="Quit"/> ran.</summary>
    internal bool QuitCalled { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="Dispose"/> ran.</summary>
    internal bool Disposed => _disposed;

    /// <summary>Gets a value indicating whether the cursor is visible.</summary>
    internal bool CursorVisible { get; private set; } = true;

    /// <summary>Gets a value indicating whether SDL text input is active.</summary>
    internal bool TextInputActive { get; private set; }

    /// <summary>Gets or sets the mouse state returned by <see cref="GetMouseState"/>.</summary>
    internal SdlMouseState MouseState { get; set; }

    /// <summary>Gets or sets a value indicating whether created windows start with input focus.</summary>
    internal bool StartFocused { get; set; }

    /// <summary>Gets or sets the scale applied to window points when a window is created or resized.</summary>
    internal float WindowScale { get; set; } = 1f;

    /// <summary>Gets or sets the message <see cref="InitializeVideo"/> fails with.</summary>
    internal string? InitializeFailure { get; set; }

    /// <summary>Gets or sets the message <see cref="CreateWindow"/> fails with.</summary>
    internal string? CreateWindowFailure { get; set; }

    /// <inheritdoc/>
    public string VideoDriver => VideoDriverName;

    /// <inheritdoc/>
    public string GetError()
    {
        ThrowIfDisposed();
        return ErrorText;
    }

    /// <inheritdoc/>
    public bool SetHint(string name, string value)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            _calls.Add($"SetHint({name}={value})");
            Hints.Add((name, value));
        }

        return true;
    }

    /// <inheritdoc/>
    public void InitializeVideo()
    {
        ThrowIfDisposed();
        InitializeVideoThreadId = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            _calls.Add("InitializeVideo");
        }

        if (InitializeFailure is not null)
        {
            throw new Bork2dException(InitializeFailure);
        }

        _videoInitialized = true;
    }

    /// <inheritdoc/>
    public void Quit()
    {
        RecordCall("Quit");
        QuitCalled = true;
        _videoInitialized = false;
    }

    /// <inheritdoc/>
    public ISdlWindow CreateWindow(string title, int width, int height, SdlWindowFlags flags)
    {
        ThrowIfDisposed();
        CreateWindowThreadId = Environment.CurrentManagedThreadId;
        RecordCall($"CreateWindow({title},{width},{height},{(uint)flags:X8})");

        if (CreateWindowFailure is not null)
        {
            throw new Bork2dException(CreateWindowFailure);
        }

        var initialFlags = flags | SdlWindowFlags.Shown;
        if (StartFocused)
        {
            initialFlags |= SdlWindowFlags.InputFocus;
        }

        var window = new FakeSdlWindow(
            (uint)(Windows.Count + 1),
            title,
            new SdlWindowSize(width, height),
            WindowScale,
            initialFlags,
            RecordCall);
        Windows.Add(window);
        return window;
    }

    /// <inheritdoc/>
    public bool TryPollEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        ThrowIfEventPollFails();
        lock (_gate)
        {
            return _events.TryDequeue(out sdlEvent);
        }
    }

    /// <inheritdoc/>
    public bool TryWaitEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        ThrowIfEventPollFails();
        while (true)
        {
            _eventAvailable.Wait();
            lock (_gate)
            {
                if (_events.TryDequeue(out sdlEvent))
                {
                    return true;
                }

                if (_disposed)
                {
                    return false;
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool TryPushEvent(in SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            _events.Enqueue(sdlEvent);
        }

        _eventAvailable.Release();
        return true;
    }

    /// <inheritdoc/>
    public int RegisterEvents(int count)
    {
        ThrowIfDisposed();
        RecordCall($"RegisterEvents({count})");
        var first = _nextUserEventType;
        _nextUserEventType += count;
        return first;
    }

    /// <inheritdoc/>
    public void ShowCursor(bool visible)
    {
        ThrowIfDisposed();
        RecordCall($"ShowCursor({visible})");
        if (ShowCursorFailure is not null)
        {
            throw new Bork2dException(ShowCursorFailure);
        }

        CursorVisible = visible;
    }

    /// <summary>Gets or sets the message <see cref="ShowCursor"/> fails with.</summary>
    internal string? ShowCursorFailure { get; set; }

    /// <inheritdoc/>
    public void StartTextInput()
    {
        ThrowIfDisposed();
        TextInputActive = true;
    }

    /// <inheritdoc/>
    public void StopTextInput()
    {
        ThrowIfDisposed();
        TextInputActive = false;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetKeyboardState()
    {
        ThrowIfDisposed();
        return _keyboardState;
    }

    /// <inheritdoc/>
    public SdlMouseState GetMouseState()
    {
        ThrowIfDisposed();
        return MouseState;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeThreadId = Environment.CurrentManagedThreadId;
        Quit();
        lock (_gate)
        {
            _disposed = true;
            _calls.Add("Dispose");
        }

        _eventAvailable.Release();
    }

    /// <summary>Adds an event to the queue and wakes a blocked wait.</summary>
    /// <param name="sdlEvent">The event to append.</param>
    internal void Enqueue(SdlEvent sdlEvent)
    {
        lock (_gate)
        {
            _events.Enqueue(sdlEvent);
        }

        _eventAvailable.Release();
    }

    /// <summary>Records one seam call for ordering assertions.</summary>
    /// <param name="call">The call description.</param>
    internal void RecordCall(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }

        // Invoked outside the lock: the callback may block until another thread observes it.
        CallRecorded?.Invoke(call);
    }

    /// <summary>Sets one keyboard scancode in the SDL keyboard state.</summary>
    /// <param name="scancode">The physical scancode.</param>
    /// <param name="isDown">Whether the key is held.</param>
    internal void SetKey(int scancode, bool isDown)
        => _keyboardState[scancode] = isDown ? (byte)1 : (byte)0;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfEventPollFails()
    {
        if (EventPollFailure is { } message)
        {
            throw new Bork2dException(message);
        }
    }
}
