namespace Bork2d.Engine.Rendering;

/// <summary>
/// Selects how a camera clears its viewport before drawing.
/// </summary>
public enum ClearKind
{
    /// <summary>
    /// Preserve earlier cameras' output and the safety clear.
    /// </summary>
    None,

    /// <summary>
    /// Reserved for a depth clear; v1 has no depth attachment, so this is a no-op when rendering
    /// lands (M8).
    /// </summary>
    Depth,

    /// <summary>
    /// Clear the camera viewport to the camera's <c>ClearColor</c>.
    /// </summary>
    Color,
}
