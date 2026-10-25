using Bork2d.Api;
using Bork2d.Runtime;

namespace Bork2d.Tests;

/// <summary>
/// An in-memory <see cref="ISdlApi"/> that drives window creation, commands, event order, focus,
/// minimize/resize transitions, and input state without loading SDL.
/// </summary>
/// <remarks>
/// The fake is the deterministic platform for windowed runtime tests. It never touches the native
/// library or a desktop session. Events are queued in call order; <see cref="TryPollEvent"/> and
/// <see cref="TryWaitEvent"/> drain the queue in that order, and <see cref="TryPushEvent"/> appends
/// to it so tests can simulate close and wake-up events.
/// </remarks>
internal sealed class FakeSdlApi : ISdlApi
{
    private readonly Queue<SdlEvent> _events = new();
    private readonly byte[] _keyboardState = new byte[512];
    private int _nextUserEventType = 0x8000;
    private bool _videoInitialized;
    private bool _disposed;

    /// <summary>Gets the windows created through <see cref="CreateWindow"/> in creation order.</summary>
    internal List<FakeSdlWindow> Windows { get; } = [];

    /// <summary>Gets the hints set through <see cref="SetHint"/> in call order.</summary>
    internal List<(string Name, string Value)> Hints { get; } = [];

    /// <summary>Gets or sets the version reported by <see cref="Version"/>.</summary>
    public SdlVersion Version { get; set; } = new(2, 30, 8);

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

    /// <summary>Gets or sets the scale applied to window points when a window is created or resized.</summary>
    internal float WindowScale { get; set; } = 1f;

    /// <summary>Gets or sets the message <see cref="InitializeVideo"/> fails with.</summary>
    internal string? InitializeFailure { get; set; }

    /// <summary>Gets or sets the message <see cref="CreateWindow"/> fails with.</summary>
    internal string? CreateWindowFailure { get; set; }

    /// <inheritdoc/>
    public string GetError()
    {
        ThrowIfDisposed();
        return string.Empty;
    }

    /// <inheritdoc/>
    public bool SetHint(string name, string value)
    {
        ThrowIfDisposed();
        Hints.Add((name, value));
        return true;
    }

    /// <inheritdoc/>
    public void InitializeVideo()
    {
        ThrowIfDisposed();
        if (InitializeFailure is not null)
        {
            throw new Bork2dException(InitializeFailure);
        }

        _videoInitialized = true;
    }

    /// <inheritdoc/>
    public void Quit()
    {
        QuitCalled = true;
        _videoInitialized = false;
    }

    /// <inheritdoc/>
    public ISdlWindow CreateWindow(string title, int width, int height, SdlWindowFlags flags)
    {
        ThrowIfDisposed();
        if (CreateWindowFailure is not null)
        {
            throw new Bork2dException(CreateWindowFailure);
        }

        var window = new FakeSdlWindow(
            (uint)(Windows.Count + 1),
            title,
            new SdlWindowSize(width, height),
            WindowScale,
            flags | SdlWindowFlags.Shown);
        Windows.Add(window);
        return window;
    }

    /// <inheritdoc/>
    public bool TryPollEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        return _events.TryDequeue(out sdlEvent);
    }

    /// <inheritdoc/>
    public bool TryWaitEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        return _events.TryDequeue(out sdlEvent);
    }

    /// <inheritdoc/>
    public bool TryPushEvent(in SdlEvent sdlEvent)
    {
        ThrowIfDisposed();
        _events.Enqueue(sdlEvent);
        return true;
    }

    /// <inheritdoc/>
    public int RegisterEvents(int count)
    {
        ThrowIfDisposed();
        var first = _nextUserEventType;
        _nextUserEventType += count;
        return first;
    }

    /// <inheritdoc/>
    public void ShowCursor(bool visible)
    {
        ThrowIfDisposed();
        CursorVisible = visible;
    }

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

        Quit();
        _disposed = true;
    }

    /// <summary>Adds an event to the queue.</summary>
    /// <param name="sdlEvent">The event to append.</param>
    internal void Enqueue(SdlEvent sdlEvent) => _events.Enqueue(sdlEvent);

    /// <summary>Sets one keyboard scancode in the SDL keyboard state.</summary>
    /// <param name="scancode">The physical scancode.</param>
    /// <param name="isDown">Whether the key is held.</param>
    internal void SetKey(int scancode, bool isDown)
        => _keyboardState[scancode] = isDown ? (byte)1 : (byte)0;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
