namespace Bork2d.Runtime;

/// <summary>
/// The SDL events the Bork2d platform consumes, flattened for the internal seam.
/// </summary>
/// <remarks>
/// <para>
/// SDL reports every window event as <c>SDL_WINDOWEVENT</c> plus a sub-type. The seam flattens the
/// sub-types into distinct members so later work packages switch on one value and fakes can emit
/// events without reproducing the SDL union.
/// </para>
/// <para>
/// <see cref="Quit"/>, the key/mouse/text members, and <see cref="UserEvent"/> reuse the SDL event
/// type numbers; the window members use adjacent values starting at <c>0x200</c> because SDL uses a
/// single type number for all of them. The values are internal and never serialized.
/// </para>
/// </remarks>
internal enum SdlEventType
{
    /// <summary>An event the platform does not consume.</summary>
    Unknown = 0,

    /// <summary><c>SDL_QUIT</c>: the window close button or an explicit quit request.</summary>
    Quit = 0x100,

    /// <summary><c>SDL_WINDOWEVENT_RESIZED</c>: the user resized the window.</summary>
    WindowResized = 0x200,

    /// <summary><c>SDL_WINDOWEVENT_SIZE_CHANGED</c>: the window size changed for any reason.</summary>
    WindowSizeChanged,

    /// <summary><c>SDL_WINDOWEVENT_MINIMIZED</c>: the window was minimized.</summary>
    WindowMinimized,

    /// <summary><c>SDL_WINDOWEVENT_MAXIMIZED</c>: the window was maximized.</summary>
    WindowMaximized,

    /// <summary><c>SDL_WINDOWEVENT_RESTORED</c>: the window was restored from minimized/maximized.</summary>
    WindowRestored,

    /// <summary><c>SDL_WINDOWEVENT_FOCUS_GAINED</c>: the window gained keyboard focus.</summary>
    WindowFocusGained,

    /// <summary><c>SDL_WINDOWEVENT_FOCUS_LOST</c>: the window lost keyboard focus.</summary>
    WindowFocusLost,

    /// <summary><c>SDL_WINDOWEVENT_CLOSE</c>: the window close button was pressed.</summary>
    WindowClose,

    /// <summary><c>SDL_KEYDOWN</c>: a key went down.</summary>
    KeyDown = 0x300,

    /// <summary><c>SDL_KEYUP</c>: a key went up.</summary>
    KeyUp,

    /// <summary><c>SDL_TEXTINPUT</c>: committed UTF-8 text input.</summary>
    TextInput = 0x303,

    /// <summary><c>SDL_MOUSEMOTION</c>: the mouse moved.</summary>
    MouseMotion = 0x400,

    /// <summary><c>SDL_MOUSEBUTTONDOWN</c>: a mouse button went down.</summary>
    MouseButtonDown,

    /// <summary><c>SDL_MOUSEBUTTONUP</c>: a mouse button went up.</summary>
    MouseButtonUp,

    /// <summary><c>SDL_MOUSEWHEEL</c>: the wheel moved.</summary>
    MouseWheel,

    /// <summary>An event from <c>SDL_RegisterEvents</c>.</summary>
    UserEvent = 0x8000,
}
