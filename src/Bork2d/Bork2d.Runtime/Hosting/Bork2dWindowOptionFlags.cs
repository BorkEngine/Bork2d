namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Tracks which window-only builder options were set so validation can reject them in
/// headless and offscreen modes.
/// </summary>
[Flags]
internal enum Bork2dWindowOptionFlags
{
    /// <summary>No window-only option was set.</summary>
    None = 0,

    /// <summary><c>WithTitle</c> was called.</summary>
    Title = 1 << 0,

    /// <summary><c>WithSize</c> was called.</summary>
    Size = 1 << 1,

    /// <summary><c>WithResizable</c> was called.</summary>
    Resizable = 1 << 2,

    /// <summary><c>WithFullscreen</c> was called.</summary>
    Fullscreen = 1 << 3,

    /// <summary><c>WithVSync</c> was called.</summary>
    VSync = 1 << 4,
}
