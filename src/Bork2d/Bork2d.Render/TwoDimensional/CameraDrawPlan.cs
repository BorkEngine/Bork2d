using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// One valid camera's position in a frame plan: its rounded pixel projection, world culling bounds,
/// load op, clear color, culling mask, its contiguous sprite/glyph instance and line buffer ranges, and
/// its contiguous draw-run range.
/// </summary>
/// <remarks>
/// Sprite and glyph records are written into the shared frame instance buffer, the line vertices and
/// indices are written into the frame line vertex/index buffers, and the runs reference those buffers
/// in draw order. All ranges are absolute frame-buffer positions.
/// </remarks>
/// <param name="Projection">The validated rounded pixel viewport and view-projection matrix.</param>
/// <param name="WorldBounds">
/// The world-space AABB of the camera's visible rectangle, used to cull 2D records conservatively.
/// </param>
/// <param name="Clear">The camera's clear mode (<c>Color</c>, <c>None</c>, or <c>Depth</c>).</param>
/// <param name="ClearColor">The linear RGBA clear value used by <c>Clear.Color</c>.</param>
/// <param name="CullingMask">The camera's layer mask used to filter the captured records.</param>
/// <param name="FirstInstance">
/// The first sprite or glyph instance record for this camera in the shared frame instance buffer.
/// </param>
/// <param name="InstanceCount">
/// The total number of sprite and glyph instance records this camera draws; zero means none.
/// </param>
/// <param name="FirstVertex">The first line vertex for this camera in the frame vertex buffer.</param>
/// <param name="VertexCount">The number of line vertices this camera draws; zero means none.</param>
/// <param name="FirstIndex">The first line index for this camera in the frame index buffer.</param>
/// <param name="IndexCount">The number of line indices this camera draws; zero means none.</param>
/// <param name="FirstRun">The first <see cref="DrawRun"/> for this camera in the frame plan.</param>
/// <param name="RunCount">The number of runs this camera records; zero means no draw.</param>
internal readonly record struct CameraDrawPlan(
    CameraProjectionResult Projection,
    Bounds WorldBounds,
    ClearKind Clear,
    Color ClearColor,
    uint CullingMask,
    int FirstInstance,
    int InstanceCount,
    int FirstVertex,
    int VertexCount,
    int FirstIndex,
    int IndexCount,
    int FirstRun,
    int RunCount);
