using System.Numerics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// Pure, allocation-free camera projection math shared by <c>Camera&lt;TLayer&gt;</c> and
/// <c>Bork2d.Render</c> (M8 §6.3).
/// </summary>
/// <remarks>
/// <para>
/// World space is +X right/+Y up. The camera's world matrix is the inverse-view source, <c>Size</c>
/// is the half visible height, and the rounded pixel viewport supplies the aspect. The only Y
/// conversion is the negation of view-space Y for Vulkan's Y-down clip space.
/// </para>
/// <para>
/// The helper is pure and thread-safe: it reads only its arguments, allocates nothing, and never
/// returns NaN for a valid camera. Points outside the viewport extrapolate rather than clamp.
/// </para>
/// </remarks>
internal static class CameraProjection
{
    /// <summary>The allowed deviation of a camera's world scale from 1.</summary>
    internal const float ScaleEpsilon = 1e-4f;

    /// <summary>
    /// Resolves a camera's rounded pixel viewport, view/projection matrices, and visible world size.
    /// </summary>
    /// <param name="framebufferSize">The framebuffer width/height in physical pixels.</param>
    /// <param name="cameraWorld">The camera's local-to-world matrix.</param>
    /// <param name="size">The half visible height in world units.</param>
    /// <param name="viewport">The normalized <c>(x, y, width, height)</c> viewport.</param>
    /// <param name="clear">The clear mode; only <c>None</c>/<c>Depth</c>/<c>Color</c> are valid.</param>
    /// <param name="clearColor">The linear RGBA clear color used by <c>Clear.Color</c>.</param>
    /// <param name="projection">The resolved projection, valid or invalid.</param>
    /// <returns><see langword="true"/> when the camera is valid.</returns>
    internal static bool TryCreate(
        Vector2 framebufferSize,
        in Matrix3x2 cameraWorld,
        float size,
        Vector4 viewport,
        ClearKind clear,
        Color clearColor,
        out CameraProjectionResult projection)
    {
        projection = default;
        if (!float.IsFinite(framebufferSize.X)
            || !float.IsFinite(framebufferSize.Y)
            || framebufferSize.X <= 0f
            || framebufferSize.Y <= 0f)
        {
            return Fail(CameraInvalidReason.Framebuffer, out projection);
        }

        if (!float.IsFinite(size) || size <= 0f)
        {
            return Fail(CameraInvalidReason.Size, out projection);
        }

        if (!float.IsFinite(viewport.X)
            || !float.IsFinite(viewport.Y)
            || !float.IsFinite(viewport.Z)
            || !float.IsFinite(viewport.W)
            || viewport.Z <= 0f
            || viewport.W <= 0f)
        {
            return Fail(CameraInvalidReason.Viewport, out projection);
        }

        if (clear is not (ClearKind.None or ClearKind.Depth or ClearKind.Color))
        {
            return Fail(CameraInvalidReason.ClearKind, out projection);
        }

        if (clear == ClearKind.Color
            && (!float.IsFinite(clearColor.R)
                || !float.IsFinite(clearColor.G)
                || !float.IsFinite(clearColor.B)
                || !float.IsFinite(clearColor.A)))
        {
            return Fail(CameraInvalidReason.ClearColor, out projection);
        }

        int left = RoundAndClamp(viewport.X * framebufferSize.X, framebufferSize.X);
        int top = RoundAndClamp(viewport.Y * framebufferSize.Y, framebufferSize.Y);
        int right = RoundAndClamp(
            (viewport.X + viewport.Z) * framebufferSize.X,
            framebufferSize.X);
        int bottom = RoundAndClamp(
            (viewport.Y + viewport.W) * framebufferSize.Y,
            framebufferSize.Y);
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return Fail(CameraInvalidReason.EmptyRect, out projection);
        }

        if (!Matrix3x2.Invert(cameraWorld, out Matrix3x2 worldToView))
        {
            return Fail(CameraInvalidReason.SingularMatrix, out projection);
        }

        float scaleX = new Vector2(cameraWorld.M11, cameraWorld.M12).Length();
        float scaleY = new Vector2(cameraWorld.M21, cameraWorld.M22).Length();
        if (MathF.Abs(scaleX - 1f) > ScaleEpsilon || MathF.Abs(scaleY - 1f) > ScaleEpsilon)
        {
            return Fail(CameraInvalidReason.NonUnitScale, out projection);
        }

        float aspect = width / (float)height;
        var view = new Matrix4x4(
            worldToView.M11,
            worldToView.M12,
            0f,
            0f,
            worldToView.M21,
            worldToView.M22,
            0f,
            0f,
            0f,
            0f,
            1f,
            0f,
            worldToView.M31,
            worldToView.M32,
            0f,
            1f);
        Matrix4x4 scale = Matrix4x4.CreateScale(1f / (size * aspect), -1f / size, 1f);
        Matrix4x4 viewProjection = Matrix4x4.Multiply(view, scale);

        projection = new CameraProjectionResult(
            IsValid: true,
            Reason: CameraInvalidReason.None,
            WorldToView: worldToView,
            ViewToWorld: cameraWorld,
            ViewProjection: viewProjection,
            Left: left,
            Top: top,
            Width: width,
            Height: height,
            Aspect: aspect,
            WorldSize: new Vector2(2f * size * aspect, 2f * size));
        return true;
    }

    /// <summary>
    /// Maps a world point to framebuffer pixels (top-left origin, viewport offset included).
    /// </summary>
    /// <param name="projection">A valid camera projection.</param>
    /// <param name="world">The finite world-space point.</param>
    /// <returns>The framebuffer-pixel position; outside-viewport points extrapolate.</returns>
    internal static Vector2 WorldToFramebuffer(in CameraProjectionResult projection, Vector2 world)
    {
        Vector2 view = Vector2.Transform(world, projection.WorldToView);
        float ndcX = view.X / (projection.WorldSize.X * 0.5f);
        float ndcY = -view.Y / (projection.WorldSize.Y * 0.5f);
        return new Vector2(
            projection.Left + (((ndcX * 0.5f) + 0.5f) * projection.Width),
            projection.Top + (((ndcY * 0.5f) + 0.5f) * projection.Height));
    }

    /// <summary>
    /// Maps a framebuffer-pixel point to world space; points outside the viewport extrapolate.
    /// </summary>
    /// <param name="projection">A valid camera projection.</param>
    /// <param name="screen">The finite framebuffer-pixel position (top-left origin).</param>
    /// <returns>The world-space point.</returns>
    internal static Vector2 FramebufferToWorld(in CameraProjectionResult projection, Vector2 screen)
    {
        float ndcX = (((screen.X - projection.Left) / projection.Width) * 2f) - 1f;
        float ndcY = (((screen.Y - projection.Top) / projection.Height) * 2f) - 1f;
        var view = new Vector2(
            ndcX * (projection.WorldSize.X * 0.5f),
            -ndcY * (projection.WorldSize.Y * 0.5f));
        return Vector2.Transform(view, projection.ViewToWorld);
    }

    /// <summary>
    /// Gets a human-readable description of an invalid-camera reason for diagnostics and the
    /// public helper exception message.
    /// </summary>
    /// <param name="reason">The invalid reason.</param>
    /// <returns>A short description.</returns>
    internal static string Describe(CameraInvalidReason reason) => reason switch
    {
        CameraInvalidReason.None => "valid",
        CameraInvalidReason.Framebuffer => "the framebuffer size is not finite or not positive",
        CameraInvalidReason.Size => "Size is not finite or not positive",
        CameraInvalidReason.Viewport => "Viewport is not finite or has a non-positive width/height",
        CameraInvalidReason.EmptyRect => "the rounded pixel viewport is empty",
        CameraInvalidReason.ClearKind => "Clear is not a defined ClearKind value",
        CameraInvalidReason.ClearColor => "ClearColor is not finite",
        CameraInvalidReason.SingularMatrix => "the camera world matrix is singular",
        CameraInvalidReason.NonUnitScale => "the camera world matrix has a non-unit scale",
        _ => "the camera values are invalid",
    };

    private static bool Fail(CameraInvalidReason reason, out CameraProjectionResult projection)
    {
        projection = new CameraProjectionResult(
            IsValid: false,
            Reason: reason,
            WorldToView: default,
            ViewToWorld: default,
            ViewProjection: default,
            Left: 0,
            Top: 0,
            Width: 0,
            Height: 0,
            Aspect: 0f,
            WorldSize: default);
        return false;
    }

    private static int RoundAndClamp(float normalized, float extent)
    {
        // The normalized coordinate is finite but a product/sum may overflow to infinity; clamping
        // both ends keeps the float-to-int conversion defined.
        float rounded = MathF.Floor(normalized + 0.5f);
        if (float.IsNaN(rounded) || rounded <= 0f)
        {
            return 0;
        }

        if (rounded >= extent)
        {
            return (int)extent;
        }

        return (int)rounded;
    }
}
