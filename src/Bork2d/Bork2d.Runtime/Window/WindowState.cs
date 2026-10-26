using Bork2d.Runtime.Sdl;

namespace Bork2d.Runtime.Window;

/// <summary>
/// The immutable confirmed window state published by the OS/window thread.
/// </summary>
/// <remarks>
/// The platform replaces the instance atomically after applying a command or handling a window
/// event; getters read the latest instance without blocking and never see a partially updated
/// value. The snapshot deliberately contains no SDL handles and no thread-affine data.
/// </remarks>
internal sealed class WindowState
{
    /// <summary>Initializes a new instance of the <see cref="WindowState"/> class.</summary>
    /// <param name="title">The confirmed title.</param>
    /// <param name="windowPoints">The window size in window points.</param>
    /// <param name="framebufferSize">The framebuffer size in physical pixels.</param>
    /// <param name="scale">The framebuffer-pixels-per-window-point scale.</param>
    /// <param name="fullscreen">Whether the window is in borderless desktop fullscreen.</param>
    /// <param name="cursorVisible">Whether the cursor is visible.</param>
    /// <param name="minimized">Whether the window is minimized.</param>
    /// <param name="focused">Whether the window has keyboard focus.</param>
    /// <param name="visible">Whether the window is visible.</param>
    /// <param name="alive">Whether the window still exists.</param>
    internal WindowState(
        string title,
        SdlWindowSize windowPoints,
        SdlWindowSize framebufferSize,
        float scale,
        bool fullscreen,
        bool cursorVisible,
        bool minimized,
        bool focused,
        bool visible,
        bool alive)
    {
        Title = title;
        WindowPoints = windowPoints;
        FramebufferSize = framebufferSize;
        Scale = scale;
        Fullscreen = fullscreen;
        CursorVisible = cursorVisible;
        Minimized = minimized;
        Focused = focused;
        Visible = visible;
        Alive = alive;
    }

    /// <summary>Gets the confirmed window title.</summary>
    internal string Title { get; }

    /// <summary>Gets the window size in window points.</summary>
    internal SdlWindowSize WindowPoints { get; }

    /// <summary>Gets the framebuffer size in physical pixels; zero until the first confirmation.</summary>
    internal SdlWindowSize FramebufferSize { get; }

    /// <summary>Gets the framebuffer-pixels-per-window-point scale.</summary>
    internal float Scale { get; }

    /// <summary>Gets a value indicating whether the window is in borderless desktop fullscreen.</summary>
    internal bool Fullscreen { get; }

    /// <summary>Gets a value indicating whether the operating-system cursor is visible.</summary>
    internal bool CursorVisible { get; }

    /// <summary>Gets a value indicating whether the window is minimized.</summary>
    internal bool Minimized { get; }

    /// <summary>Gets a value indicating whether the window has keyboard focus.</summary>
    internal bool Focused { get; }

    /// <summary>Gets a value indicating whether the window is visible.</summary>
    internal bool Visible { get; }

    /// <summary>Gets a value indicating whether the window still exists.</summary>
    internal bool Alive { get; }

    /// <summary>Returns a copy of this state with a new <see cref="Alive"/> value.</summary>
    /// <param name="alive">Whether the window still exists.</param>
    /// <returns>The updated snapshot.</returns>
    internal WindowState WithAlive(bool alive)
        => new(
            Title,
            WindowPoints,
            FramebufferSize,
            Scale,
            Fullscreen,
            CursorVisible,
            Minimized,
            Focused,
            Visible,
            alive);
}
