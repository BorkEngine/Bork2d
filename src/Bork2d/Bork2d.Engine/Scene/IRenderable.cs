using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A scene transform that draws and exposes its world-space bounds for conservative culling.
/// </summary>
/// <remarks>
/// <para>
/// The bounds are a read-only world-space AABB of what the renderable currently draws, derived from
/// its live scene state (the world matrix and, for sprites, the frozen render data). They never
/// cross the tick/render boundary except where a snapshot carries them.
/// </para>
/// <para>
/// Reading bounds never throws for an invalid renderable: a camera that cannot be projected
/// (invalid values, a headless app, or a shut-down window) and a sprite with no drawable frozen data
/// return <see cref="Bounds.Empty"/>. Callers must treat empty bounds as "unknown" and never as
/// "outside".
/// </para>
/// </remarks>
public interface IRenderable : ITransform
{
    /// <summary>
    /// Gets the world-space axis-aligned bounding box of what this renderable draws.
    /// </summary>
    /// <value>
    /// The inclusive world-space AABB in absolute world units, or <see cref="Bounds.Empty"/> when
    /// the renderable is invalid, hidden, or cannot currently be projected.
    /// </value>
    Bounds Bounds { get; }
}
