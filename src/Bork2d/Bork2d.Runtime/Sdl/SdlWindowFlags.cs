namespace Bork2d.Runtime;

/// <summary>
/// The SDL window flags the Bork2d platform uses when it creates a window.
/// </summary>
/// <remarks>
/// Values match <c>SDL_WindowFlags</c> so the adapter forwards them unchanged. Only the flags the
/// M2 window needs are declared; a window is visible unless the hidden flag is set. The flags are
/// internal and never serialized.
/// </remarks>
[Flags]
internal enum SdlWindowFlags : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The window is fullscreen (<c>SDL_WINDOW_FULLSCREEN</c>).</summary>
    Fullscreen = 0x0000_0001,

    /// <summary>The window is visible (<c>SDL_WINDOW_SHOWN</c>); SDL sets it at creation.</summary>
    Shown = 0x0000_0004,

    /// <summary>The user may resize the window (<c>SDL_WINDOW_RESIZABLE</c>).</summary>
    Resizable = 0x0000_0020,

    /// <summary>Borderless desktop fullscreen on the current display (<c>SDL_WINDOW_FULLSCREEN_DESKTOP</c>).</summary>
    FullscreenDesktop = 0x0000_1001,

    /// <summary>The window has a high-DPI framebuffer (<c>SDL_WINDOW_ALLOW_HIGHDPI</c>).</summary>
    AllowHighDpi = 0x0000_2000,

    /// <summary>The window can host a Vulkan surface (<c>SDL_WINDOW_VULKAN</c>).</summary>
    Vulkan = 0x1000_0000,
}
