using System.Runtime.InteropServices;
using Bork2d.Engine;
using Silk.NET.Core.Loader;
using Silk.NET.SDL;
using SilkNet = Silk.NET.SDL;

namespace Bork2d.Runtime.Sdl;

/// <summary>
/// The production <see cref="ISdlApi"/> over Silk.NET's generated SDL bindings.
/// </summary>
/// <remarks>
/// <para>
/// Create one instance on the OS thread that will own the window, set hints, call
/// <see cref="InitializeVideo"/>, use the windows, destroy them, and dispose the instance to quit
/// SDL and free its native handle. Each instance owns its own <see cref="Silk.NET.SDL.Sdl"/> instance, so a
/// later app in the same process creates a new one.
/// </para>
/// <para>
/// The adapter maps native events to <see cref="SdlEvent"/> and converts SDL failures to
/// <see cref="Bork2dException"/> with the <c>SDL_GetError</c> text. It performs no reflection or
/// dynamic code and keeps no thread affinity.
/// </para>
/// </remarks>
internal sealed unsafe class SilkSdlApi : ISdlApi
{
    /// <summary><c>SDL_INIT_VIDEO</c>; video only, never audio, game controller, or haptics.</summary>
    private const uint SdlInitVideo = 0x0000_0020;

    private readonly SilkNet.Sdl _sdl;
    private bool _videoInitialized;
    private bool _vulkanLoaded;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SilkSdlApi"/> class.</summary>
    /// <exception cref="Bork2dException">The SDL2 native library could not be loaded.</exception>
    internal SilkSdlApi()
    {
        _sdl = CreateSdl();

        var version = default(Silk.NET.SDL.Version);
        _sdl.GetVersion(&version);
        Version = new SdlVersion(version.Major, version.Minor, version.Patch);
    }

    /// <inheritdoc/>
    public SdlVersion Version { get; }

    /// <inheritdoc/>
    public string VideoDriver
    {
        get
        {
            ThrowIfDisposed();

            var driver = _sdl.GetCurrentVideoDriver();
            return driver is null ? string.Empty : Marshal.PtrToStringUTF8((nint)driver) ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public string GetError()
    {
        ThrowIfDisposed();
        return SdlError.GetMessage(_sdl);
    }

    /// <inheritdoc/>
    public bool SetHint(string name, string value)
    {
        ThrowIfDisposed();
        return _sdl.SetHint(name, value) == SdlBool.True;
    }

    /// <inheritdoc/>
    public void InitializeVideo()
    {
        ThrowIfDisposed();
        if (_videoInitialized)
        {
            return;
        }

        if (_sdl.Init(SdlInitVideo) != 0)
        {
            throw SdlError.Create(_sdl, "SDL_Init(SDL_INIT_VIDEO)");
        }

        _videoInitialized = true;
    }

    /// <inheritdoc/>
    public void Quit()
    {
        if (_disposed || !_videoInitialized)
        {
            return;
        }

        _videoInitialized = false;
        _sdl.Quit();
    }

    /// <inheritdoc/>
    public ISdlWindow CreateWindow(string title, int width, int height, SdlWindowFlags flags)
    {
        ThrowIfDisposed();

        var window = _sdl.CreateWindow(
            title,
            SilkNet.Sdl.WindowposCentered,
            SilkNet.Sdl.WindowposCentered,
            width,
            height,
            (uint)flags);
        if (window == null)
        {
            throw SdlError.Create(_sdl, "SDL_CreateWindow");
        }

        return new SilkSdlWindow(_sdl, window);
    }

    /// <inheritdoc/>
    public bool TryPollEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();

        var nativeEvent = default(Event);
        if (_sdl.PollEvent(&nativeEvent) <= 0)
        {
            sdlEvent = default;
            return false;
        }

        sdlEvent = Map(&nativeEvent);
        return true;
    }

    /// <inheritdoc/>
    public bool TryWaitEvent(out SdlEvent sdlEvent)
    {
        ThrowIfDisposed();

        var nativeEvent = default(Event);
        if (_sdl.WaitEvent(&nativeEvent) <= 0)
        {
            sdlEvent = default;
            return false;
        }

        sdlEvent = Map(&nativeEvent);
        return true;
    }

    /// <inheritdoc/>
    public bool TryPushEvent(in SdlEvent sdlEvent)
    {
        ThrowIfDisposed();

        var nativeEvent = default(Event);
        switch (sdlEvent.Type)
        {
            case SdlEventType.Quit:
                nativeEvent.Type = (uint)EventType.Quit;
                break;

            case SdlEventType.UserEvent:
                nativeEvent.Type = (uint)sdlEvent.UserCode;
                nativeEvent.User.Code = sdlEvent.UserCode;
                break;

            default:
                throw new NotSupportedException(
                    $"The SDL seam can push only quit and user events; '{sdlEvent.Type}' is not "
                    + "supported.");
        }

        return _sdl.PushEvent(&nativeEvent) == 1;
    }

    /// <inheritdoc/>
    public int RegisterEvents(int count)
    {
        ThrowIfDisposed();
        return unchecked((int)_sdl.RegisterEvents(count));
    }

    /// <inheritdoc/>
    public void ShowCursor(bool visible)
    {
        ThrowIfDisposed();
        if (_sdl.ShowCursor(visible ? 1 : 0) < 0)
        {
            throw SdlError.Create(_sdl, "SDL_ShowCursor");
        }
    }

    /// <inheritdoc/>
    public void StartTextInput()
    {
        ThrowIfDisposed();
        _sdl.StartTextInput();
    }

    /// <inheritdoc/>
    public void StopTextInput()
    {
        ThrowIfDisposed();
        _sdl.StopTextInput();
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetKeyboardState()
    {
        ThrowIfDisposed();

        var count = 0;
        var state = _sdl.GetKeyboardState(ref count);
        return state == null || count <= 0
            ? ReadOnlySpan<byte>.Empty
            : new ReadOnlySpan<byte>(state, count);
    }

    /// <inheritdoc/>
    public SdlMouseState GetMouseState()
    {
        ThrowIfDisposed();

        var x = 0;
        var y = 0;
        var buttons = _sdl.GetMouseState(ref x, ref y);
        return new SdlMouseState(x, y, buttons);
    }

    /// <inheritdoc/>
    public void VulkanLoadLibrary()
    {
        ThrowIfDisposed();
        if (_vulkanLoaded)
        {
            return;
        }

        // SDL_Vulkan_LoadLibrary(NULL) resolves the platform's Vulkan loader (vulkan-1.dll).
        if (_sdl.VulkanLoadLibrary((byte*)null) < 0)
        {
            throw SdlError.Create(_sdl, "SDL_Vulkan_LoadLibrary");
        }

        _vulkanLoaded = true;
    }

    /// <inheritdoc/>
    public void VulkanUnloadLibrary()
    {
        if (_disposed || !_vulkanLoaded)
        {
            return;
        }

        _vulkanLoaded = false;
        try
        {
            _sdl.VulkanUnloadLibrary();
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException
                or BadImageFormatException or SymbolLoadingException)
        {
            // The loader is already gone; shutdown must not fail because of it.
        }
    }

    /// <summary>Quits SDL, frees the native handle, and rejects further use.</summary>
    /// <remarks>
    /// Every window created by this api must be destroyed first. The call is idempotent: only the
    /// first disposal unloads the Vulkan loader, quits SDL, and disposes the
    /// <see cref="Silk.NET.SDL.Sdl"/> instance and its native context.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VulkanUnloadLibrary();
        Quit();
        _sdl.Dispose();
        _disposed = true;
    }

    private static SilkNet.Sdl CreateSdl()
    {
        try
        {
            return SilkNet.Sdl.GetApi();
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException
                or BadImageFormatException or SymbolLoadingException)
        {
            throw new Bork2dException(
                "The SDL2 native library could not be loaded. Silk.NET.SDL 2.23.0 supplies it as a "
                + "runtime-specific asset; verify the package restore and the target runtime "
                + "identifier.",
                exception);
        }
    }

    private static SdlEvent Map(Event* nativeEvent)
    {
        return (EventType)nativeEvent->Type switch
        {
            EventType.Quit => SdlEvent.Quit(),
            EventType.Windowevent => MapWindowEvent(nativeEvent),
            EventType.Keydown => SdlEvent.Key(
                SdlEventType.KeyDown,
                (int)nativeEvent->Key.Keysym.Scancode,
                nativeEvent->Key.Repeat != 0),
            EventType.Keyup => SdlEvent.Key(
                SdlEventType.KeyUp,
                (int)nativeEvent->Key.Keysym.Scancode),
            EventType.Textinput => SdlEvent.TextInput(DecodeText(nativeEvent)),
            EventType.Mousemotion => SdlEvent.MouseMotion(nativeEvent->Motion.X, nativeEvent->Motion.Y),
            EventType.Mousebuttondown => SdlEvent.MouseButton(
                nativeEvent->Button.Button,
                isDown: true,
                nativeEvent->Button.X,
                nativeEvent->Button.Y),
            EventType.Mousebuttonup => SdlEvent.MouseButton(
                nativeEvent->Button.Button,
                isDown: false,
                nativeEvent->Button.X,
                nativeEvent->Button.Y),
            EventType.Mousewheel => SdlEvent.MouseWheel(
                nativeEvent->Wheel.X,
                nativeEvent->Wheel.Y,
                nativeEvent->Wheel.Direction == (uint)MouseWheelDirection.Flipped),
            _ when nativeEvent->Type >= (uint)EventType.Userevent => SdlEvent.User((int)nativeEvent->Type),
            _ => SdlEvent.Unknown(),
        };
    }

    private static SdlEvent MapWindowEvent(Event* nativeEvent)
    {
        var type = (WindowEventID)nativeEvent->Window.Event switch
        {
            WindowEventID.Resized => SdlEventType.WindowResized,
            WindowEventID.SizeChanged => SdlEventType.WindowSizeChanged,
            WindowEventID.Minimized => SdlEventType.WindowMinimized,
            WindowEventID.Maximized => SdlEventType.WindowMaximized,
            WindowEventID.Restored => SdlEventType.WindowRestored,
            WindowEventID.FocusGained => SdlEventType.WindowFocusGained,
            WindowEventID.FocusLost => SdlEventType.WindowFocusLost,
            WindowEventID.Close => SdlEventType.WindowClose,
            _ => SdlEventType.Unknown,
        };

        return SdlEvent.Window(type, nativeEvent->Window.Data1, nativeEvent->Window.Data2);
    }

    private static string DecodeText(Event* nativeEvent)
    {
        // SDL stores a null-terminated UTF-8 string in a fixed 32-byte buffer inside the event.
        var text = nativeEvent->Text.Text;
        return text == null
            ? string.Empty
            : SdlText.Decode(new ReadOnlySpan<byte>(text, SdlText.BufferSize));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
