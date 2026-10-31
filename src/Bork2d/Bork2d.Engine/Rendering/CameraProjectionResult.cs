using System.Numerics;

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
    Vector2 WorldSize);
