using Silk.NET.SDL;
using SilkNet = Silk.NET.SDL;

namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The production <see cref="ISdlWindow"/> over one native <c>SDL_Window</c>.
/// </summary>
/// <remarks>
/// The window borrows the <see cref="Silk.NET.SDL.Sdl"/> instance from its <see cref="SilkSdlApi"/>; the api must
/// outlive the window. <see cref="Dispose"/> destroys the native window and is idempotent; every
/// other member throws <see cref="ObjectDisposedException"/> afterwards.
/// </remarks>
internal sealed unsafe class SilkSdlWindow : ISdlWindow
{
    private readonly SilkNet.Sdl _sdl;
    private SilkNet.Window* _window;

    /// <summary>Initializes a new instance of the <see cref="SilkSdlWindow"/> class.</summary>
    /// <param name="sdl">The SDL instance that created the window.</param>
    /// <param name="window">The native window pointer.</param>
    internal SilkSdlWindow(SilkNet.Sdl sdl, SilkNet.Window* window)
    {
        _sdl = sdl;
        _window = window;
        Id = sdl.GetWindowID(window);
    }

    /// <inheritdoc/>
    public uint Id { get; }

    /// <inheritdoc/>
    public SdlWindowSize Size
    {
        get
        {
            ThrowIfDisposed();

            int width;
            int height;
            _sdl.GetWindowSize(_window, &width, &height);
            return new SdlWindowSize(width, height);
        }
    }

    /// <inheritdoc/>
    public SdlWindowSize SizeInPixels
    {
        get
        {
            ThrowIfDisposed();

            int width;
            int height;
            _sdl.GetWindowSizeInPixels(_window, &width, &height);
            return new SdlWindowSize(width, height);
        }
    }

    /// <inheritdoc/>
    public SdlWindowFlags Flags
    {
        get
        {
            ThrowIfDisposed();
            return (SdlWindowFlags)_sdl.GetWindowFlags(_window);
        }
    }

    /// <inheritdoc/>
    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        _sdl.SetWindowTitle(_window, title);
    }

    /// <inheritdoc/>
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        _sdl.SetWindowSize(_window, width, height);
    }

    /// <inheritdoc/>
    public void SetFullscreen(bool fullscreen)
    {
        ThrowIfDisposed();

        var flags = fullscreen ? (uint)SdlWindowFlags.FullscreenDesktop : 0u;
        if (_sdl.SetWindowFullscreen(_window, flags) != 0)
        {
            throw SdlError.Create(_sdl, "SDL_SetWindowFullscreen");
        }
    }

    /// <summary>Destroys the native window. Idempotent.</summary>
    public void Dispose()
    {
        if (_window == null)
        {
            return;
        }

        _sdl.DestroyWindow(_window);
        _window = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_window == null, this);
    }
}
