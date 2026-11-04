namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The renderable kind a planned <see cref="DrawRun"/> draws.
/// </summary>
/// <remarks>
/// The kind participates in run grouping: two adjacent visible records share a run only when both
/// their <see cref="DrawKind"/> and their resolved shader entry match. It never participates in the
/// draw order, which is <c>(ZIndex, screen Y, screen X)</c> only.
/// </remarks>
internal enum DrawKind
{
    /// <summary>A sprite: one instanced quad draw from the frame instance buffer.</summary>
    Sprite,

    /// <summary>A line: one indexed triangle-list draw from the frame line vertex/index buffers.</summary>
    Line,
}
