namespace Bork2d.Render;

/// <summary>
/// A framebuffer or offscreen extent in pixels.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
internal readonly record struct WindowExtent(int Width, int Height)
{
    /// <summary>Gets whether the extent is zero in either dimension (minimized).</summary>
    internal bool IsZeroOrMinimized => Width <= 0 || Height <= 0;
}
