using System.Numerics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A value-only copy of one rooted camera in a single frame snapshot (Q5=E1).
/// </summary>
/// <remarks>
/// The snapshot is internal and immutable after publication. It holds exactly the camera values
/// the renderer needs: the sort key, the camera's world transform as the inverse-view source, the
/// half visible height, the normalized viewport, the layer mask, and the clear state. The M8
/// renderer derives the view and projection from these values; M7 captures them and draws
/// nothing. No camera, transform, or other live scene entity reference crosses the boundary.
/// </remarks>
/// <param name="ZIndex">
/// The camera order key; cameras apply in ascending order and ties are unspecified.
/// </param>
/// <param name="WorldMatrix">The camera's local-to-world matrix, composed through the hierarchy.</param>
/// <param name="Size">The half visible height in world units.</param>
/// <param name="Viewport">The normalized <c>(x, y, width, height)</c> viewport, top-left origin.</param>
/// <param name="CullingMask">The layer mask as a raw 32-bit mask; bit 31 survives.</param>
/// <param name="Clear">The clear mode applied to the camera viewport at draw time.</param>
/// <param name="ClearColor">The linear RGBA clear color used by <see cref="ClearKind.Color"/>.</param>
internal readonly record struct CameraSnapshot(
    int ZIndex,
    Matrix3x2 WorldMatrix,
    float Size,
    Vector4 Viewport,
    uint CullingMask,
    ClearKind Clear,
    Color ClearColor);
