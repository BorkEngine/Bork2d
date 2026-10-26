namespace Bork2d.Runtime.Window;

/// <summary>
/// The builder-derived options the windowed platform needs to create its window.
/// </summary>
/// <remarks>
/// The M2 runtime host maps <c>Bork2dAppOptions</c> into this non-generic shape so the platform
/// does not depend on the game layer type. The width and height are the requested framebuffer
/// size in physical pixels; the platform converts them to window points with the measured scale.
/// </remarks>
internal sealed class WindowPlatformOptions
{
    /// <summary>Initializes a new instance of the <see cref="WindowPlatformOptions"/> class.</summary>
    /// <param name="title">The initial window title.</param>
    /// <param name="width">The requested framebuffer width in physical pixels.</param>
    /// <param name="height">The requested framebuffer height in physical pixels.</param>
    /// <param name="resizable">Whether the user may resize the window.</param>
    /// <param name="fullscreen">Whether the window starts in borderless desktop fullscreen.</param>
    /// <param name="vsync">The stored vertical-sync preference, logged but not applied in M2.</param>
    internal WindowPlatformOptions(
        string title,
        int width,
        int height,
        bool resizable,
        bool fullscreen,
        bool vsync)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        Title = title;
        Width = width;
        Height = height;
        Resizable = resizable;
        Fullscreen = fullscreen;
        VSync = vsync;
    }

    /// <summary>Gets the initial window title.</summary>
    internal string Title { get; }

    /// <summary>Gets the requested framebuffer width in physical pixels.</summary>
    internal int Width { get; }

    /// <summary>Gets the requested framebuffer height in physical pixels.</summary>
    internal int Height { get; }

    /// <summary>Gets a value indicating whether the window may be resized by the user.</summary>
    internal bool Resizable { get; }

    /// <summary>Gets a value indicating whether the window starts in borderless desktop fullscreen.</summary>
    internal bool Fullscreen { get; }

    /// <summary>Gets the stored vertical-sync preference; it has no effect in M2.</summary>
    internal bool VSync { get; }
}
