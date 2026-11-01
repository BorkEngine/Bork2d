using System.Numerics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A camera in the scene graph: a transform plus projection and clear state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Cameras are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear
/// the owning parent to remove one, and re-attach the same instance later.
/// </para>
/// <para>
/// <see cref="IRenderable.Bounds"/> is the world-space AABB of the visible rectangle: the four
/// view-space corners <c>(±WorldSize.X/2, ±WorldSize.Y/2)</c> transformed by the camera's
/// view-to-world matrix. It returns <see cref="Bounds.Empty"/> when the camera is invalid or the
/// app is headless or has shut down, and it never throws for those cases.
/// </para>
/// </remarks>
public interface ICamera<TLayer> : IRenderable where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the normalized viewport <c>(x, y, width, height)</c> with a top-left origin.
    /// </summary>
    Vector4 Viewport { get; set; }

    /// <summary>
    /// Gets or sets the half visible height in world units.
    /// </summary>
    float Size { get; set; }

    /// <summary>
    /// Gets or sets the layer mask this camera renders; <c>0</c> sees nothing.
    /// </summary>
    TLayer CullingMask { get; set; }

    /// <summary>
    /// Gets or sets how the camera clears its viewport before drawing.
    /// </summary>
    ClearKind Clear { get; set; }

    /// <summary>
    /// Gets or sets the linear RGBA clear color used by <see cref="ClearKind.Color"/>.
    /// </summary>
    Color ClearColor { get; set; }

    /// <summary>
    /// Gets the visible world width and height for this camera's current framebuffer.
    /// </summary>
    /// <value>
    /// <c>(2 * Size * aspect, 2 * Size)</c>, where the aspect is the rounded pixel-viewport width
    /// divided by its height from the engine window's framebuffer size (physical pixels; the fixed
    /// offscreen target extent in offscreen mode).
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// The camera is invalid, or the app is headless so it has no framebuffer.
    /// </exception>
    Vector2 WorldSize { get; }

    /// <summary>
    /// Maps a world-space point to global framebuffer pixels (top-left origin).
    /// </summary>
    /// <remarks>
    /// The returned point includes the camera viewport offset and uses the same camera inverse,
    /// <see cref="Size"/>, rounded viewport/aspect, and Y flip as the renderer. A point outside the
    /// camera viewport extrapolates; it is not clamped. The camera viewport is rounded half-up and
    /// clamped to the framebuffer before use.
    /// </remarks>
    /// <param name="world">The world-space point, in world units.</param>
    /// <returns>The framebuffer-pixel position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component of <paramref name="world"/> is NaN or infinite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera is invalid, or the app is headless so it has no framebuffer.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    Vector2 WorldToScreen(Vector2 world);

    /// <summary>
    /// Maps a global framebuffer-pixel point (top-left origin) back to world space.
    /// </summary>
    /// <remarks>
    /// This is the inverse of <see cref="WorldToScreen"/> with the same rounding and exceptions. A
    /// point outside the camera viewport extrapolates; it is not clamped.
    /// </remarks>
    /// <param name="screen">The framebuffer-pixel position, in physical pixels.</param>
    /// <returns>The world-space point, in world units.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component of <paramref name="screen"/> is NaN or infinite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera is invalid, or the app is headless so it has no framebuffer.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The windowed app has shut down.</exception>
    Vector2 ScreenToWorld(Vector2 screen);

    /// <summary>
    /// Gets or sets the draw order between cameras; higher values apply later and draw on top.
    /// </summary>
    /// <value>
    /// The camera sort key. The default is <c>0</c>. Cameras are applied in ascending
    /// <see cref="ZIndex"/> order; ties are unspecified, so assign distinct values when the
    /// overlap matters.
    /// </value>
    int ZIndex { get; set; }
}
