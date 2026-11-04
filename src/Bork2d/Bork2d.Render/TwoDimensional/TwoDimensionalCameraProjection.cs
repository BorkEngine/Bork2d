using System.Numerics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// Resolves a captured camera to the 2D renderer's rounded pixel rect and 64-byte view-projection
/// push constant.
/// </summary>
/// <remarks>
/// The renderer shares the exact <see cref="CameraProjection"/> math with the public
/// <c>ICamera.WorldToScreen</c>/<c>ScreenToWorld</c>/<c>WorldSize</c> helpers, so a GPU camera and a
/// helper call can never disagree on the viewport rect, aspect, or Y flip. The same projection
/// resolves each camera-visible record's screen-space sort position, so draw order and the recorded
/// viewport always agree.
/// </remarks>
internal static class TwoDimensionalCameraProjection
{
    /// <summary>Resolves one captured camera against a framebuffer extent.</summary>
    /// <param name="camera">The captured camera values.</param>
    /// <param name="framebufferSize">The framebuffer width/height in physical pixels.</param>
    /// <param name="projection">The resolved projection, valid or invalid.</param>
    /// <returns><see langword="true"/> when the camera is valid.</returns>
    internal static bool TryCreate(
        in CameraSnapshot camera,
        Vector2 framebufferSize,
        out CameraProjectionResult projection)
    {
        return CameraProjection.TryCreate(
            framebufferSize,
            camera.WorldMatrix,
            camera.Size,
            camera.Viewport,
            camera.Clear,
            camera.ClearColor,
            out projection);
    }
}
