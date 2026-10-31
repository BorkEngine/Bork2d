using System.Numerics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Resolves a captured camera to the renderer's rounded pixel rect and 64-byte view-projection
/// push constant (M8 §6.3/§6.6).
/// </summary>
/// <remarks>
/// The renderer shares the exact <see cref="CameraProjection"/> math with the public
/// <c>ICamera.WorldToScreen</c>/<c>ScreenToWorld</c>/<c>WorldSize</c> helpers, so a GPU camera and a
/// helper call can never disagree on the viewport rect, aspect, or Y flip. WP-2 adds this bridge;
/// WP-4 records the per-camera pass that consumes it.
/// </remarks>
internal static class SpriteCameraProjection
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
