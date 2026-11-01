using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// The pure result of resolving one camera against a framebuffer (M8 §6.3).
/// </summary>
/// <remarks>
/// The same helper feeds the renderer's per-camera projection and the public
/// <c>ICamera.WorldToScreen</c>/<c>ScreenToWorld</c>/<c>WorldSize</c> members, so both use exactly
/// the same rounding, aspect, and Y flip. An invalid result has zero matrices and an empty rect;
/// callers must check <see cref="IsValid"/>.
/// </remarks>
/// <param name="IsValid">Whether the camera produced a usable projection.</param>
/// <param name="Reason">The first invalid-camera criterion that failed.</param>
/// <param name="WorldToView">The inverse of the camera world matrix (world to view space).</param>
/// <param name="ViewToWorld">The camera world matrix (view to world space).</param>
/// <param name="ViewProjection">
/// The 4x4 view-projection for the 64-byte push constant. System.Numerics uses row-vector
/// multiplication; the shader consumes the same bytes as a column-major GLSL matrix, which is the
/// equivalent transposed form.
/// </param>
/// <param name="Left">The rounded, clamped pixel left edge.</param>
/// <param name="Top">The rounded, clamped pixel top edge.</param>
/// <param name="Width">The rounded pixel viewport width.</param>
/// <param name="Height">The rounded pixel viewport height.</param>
/// <param name="Aspect">The rounded pixel aspect <c>Width / Height</c>.</param>
/// <param name="WorldSize">The visible world width/height.</param>
internal readonly record struct CameraProjectionResult(
    bool IsValid,
    CameraInvalidReason Reason,
    Matrix3x2 WorldToView,
    Matrix3x2 ViewToWorld,
    Matrix4x4 ViewProjection,
    int Left,
    int Top,
    int Width,
    int Height,
    float Aspect,
    Vector2 WorldSize)
{
    /// <summary>
    /// Gets the world-space AABB of the camera's visible rectangle.
    /// </summary>
    /// <remarks>
    /// The four view-space corners <c>(±WorldSize.X/2, ±WorldSize.Y/2)</c> are transformed by
    /// <see cref="ViewToWorld"/> and reduced to an AABB, so a rotated camera yields the larger
    /// axis-aligned box that conservatively contains its rotated view rectangle. The renderer's
    /// camera plans and the public <c>ICamera.Bounds</c> helper both use this method, so they can
    /// never disagree. An invalid projection returns <see cref="Bounds.Empty"/>.
    /// </remarks>
    /// <returns>The world-space AABB, or <see cref="Bounds.Empty"/> when the projection is invalid.</returns>
    internal Bounds CreateWorldBounds()
    {
        if (!IsValid)
        {
            return Bounds.Empty;
        }

        Vector2 half = WorldSize * 0.5f;
        Vector2 corner0 = Vector2.Transform(new Vector2(-half.X, -half.Y), ViewToWorld);
        Vector2 corner1 = Vector2.Transform(new Vector2(half.X, -half.Y), ViewToWorld);
        Vector2 corner2 = Vector2.Transform(new Vector2(half.X, half.Y), ViewToWorld);
        Vector2 corner3 = Vector2.Transform(new Vector2(-half.X, half.Y), ViewToWorld);
        Bounds bounds = Bounds.FromMinMax(
            Vector2.Min(Vector2.Min(corner0, corner1), Vector2.Min(corner2, corner3)),
            Vector2.Max(Vector2.Max(corner0, corner1), Vector2.Max(corner2, corner3)));

        // A visible-size overflow makes the corners non-finite; normalize it to the Empty sentinel
        // so callers can compare against Bounds.Empty as well as check IsEmpty.
        return bounds.IsEmpty ? Bounds.Empty : bounds;
    }
}
