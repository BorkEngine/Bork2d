using System.Numerics;

namespace Bork2d.Api.Platform;

/// <summary>
/// Controls the window of a windowed app.
/// </summary>
/// <remarks>
/// <para>
/// The interface is windowed-only. In headless and offscreen modes <c>IEngine.Window</c> returns a
/// stub whose members throw <see cref="NotSupportedException"/>. After the windowed app has shut
/// down, retained references remain valid objects but their members throw
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// <para>
/// All members may be called from any thread. Setters validate their arguments and enqueue a
/// command for the engine-owned OS/window thread; they never block on SDL and never wait for the
/// command to apply. Getters return the last confirmed state without blocking and may lag one
/// event-pump pass. Commands are coalesced per property, so the latest requested value wins while
/// the order between different properties is preserved.
/// </para>
/// <para>
/// Sizes are framebuffer physical pixels, not logical points: at Windows display scaling above
/// 100% the window's point size differs from <see cref="Size"/>. Mouse coordinates reported by
/// <c>IInput</c> use the same framebuffer-pixel unit.
/// </para>
/// </remarks>
public interface IWindow
{
    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    /// <remarks>
    /// The value must be non-null; empty and whitespace-only titles are allowed. The initial value
    /// is the builder title. The setter enqueues the title and returns; the getter returns the last
    /// confirmed title and may lag one event-pump pass.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The setter value is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// The app is headless or offscreen, so it has no window.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    string Title { get; set; }

    /// <summary>
    /// Gets or sets the framebuffer size in physical pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The getter returns the last confirmed non-zero framebuffer size, including while the window
    /// is minimized; it may lag one event-pump pass. The setter requires finite, positive width and
    /// height and converts the request to window points with the current display scale. While the
    /// window is fullscreen the setter is ignored and logs a debug message. While the window is
    /// minimized the request becomes the size restored later.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A setter component is zero, negative, NaN, or infinite.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The app is headless or offscreen, so it has no window.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    Vector2 Size { get; set; }

    /// <summary>
    /// Gets or sets whether the window is in borderless desktop fullscreen.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> requests borderless fullscreen on the primary monitor;
    /// <see langword="false"/> returns to windowed mode, restoring the previous windowed bounds
    /// (managed by SDL). The initial value comes from the builder. The setter enqueues the request
    /// and drops calls that repeat the last requested value; the getter returns the last confirmed
    /// state and may lag one event-pump pass. Leaving fullscreen restores the windowed size.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The app is headless or offscreen, so it has no window.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    bool Fullscreen { get; set; }

    /// <summary>
    /// Gets or sets whether the operating-system mouse cursor is visible.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. Bork2d never captures the mouse, never uses relative
    /// mode, and never hides the cursor automatically, including in fullscreen. The setter enqueues
    /// the request and drops calls that repeat the last requested value; the getter returns the
    /// last confirmed state and may lag one event-pump pass.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The app is headless or offscreen, so it has no window.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    bool CursorVisible { get; set; }

    /// <summary>
    /// Gets a value indicating whether the window is currently minimized.
    /// </summary>
    /// <remarks>
    /// The value is read from the last confirmed window state without blocking and may lag one
    /// event-pump pass. Ticks continue while the window is minimized, and <see cref="Size"/> keeps
    /// reporting the last non-minimized framebuffer size.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The app is headless or offscreen, so it has no window.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    bool IsMinimized { get; }
}
