using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Sprites;

/// <summary>
/// One valid camera's position in a frame plan: its rounded pixel projection, world culling bounds,
/// load op, clear color, culling mask, and its contiguous instance range (M8 §6.6/§6.8).
/// </summary>
/// <param name="Projection">The validated rounded pixel viewport and view-projection matrix.</param>
/// <param name="WorldBounds">
/// The world-space AABB of the camera's visible rectangle, used to cull sprites conservatively.
/// </param>
/// <param name="Clear">The camera's clear mode (<c>Color</c>, <c>None</c>, or <c>Depth</c>).</param>
/// <param name="ClearColor">The linear RGBA clear value used by <c>Clear.Color</c>.</param>
/// <param name="CullingMask">The camera's layer mask used to filter the sorted sprites.</param>
/// <param name="FirstInstance">The first instance record for this camera in the frame buffer.</param>
/// <param name="InstanceCount">The number of records this camera draws; zero means no draw.</param>
internal readonly record struct CameraDrawPlan(
    CameraProjectionResult Projection,
    Bounds WorldBounds,
    ClearKind Clear,
    Color ClearColor,
    uint CullingMask,
    int FirstInstance,
    int InstanceCount);
