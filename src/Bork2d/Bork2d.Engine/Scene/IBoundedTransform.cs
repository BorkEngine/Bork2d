using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A scene transform with a world-space axis-aligned bounding box for conservative culling.
/// </summary>
/// <remarks>
/// <para>
/// Bounded transforms are the shared base of cameras and 2D renderables. A renderable is drawn and
/// exposes the bounds of what it draws; a camera is not drawn and exposes the bounds of its visible
/// world rectangle instead.
/// </para>
/// <para>
/// The bounds are a read-only world-space AABB derived from live scene state (the world matrix and,
/// for sprites, the frozen render data). They never cross the tick/render boundary except where a
/// snapshot carries them.
/// </para>
/// <para>
/// Reading bounds never throws for an invalid bounded transform: a camera that cannot be projected
/// (invalid values, a headless app, or a shut-down window) and a renderable with no drawable frozen
/// data return <see cref="Bounds.Empty"/>. Callers must treat empty bounds as "unknown" and never as
/// "outside".
/// </para>
/// </remarks>
public interface IBoundedTransform : ITransform
{
    /// <summary>
    /// Gets the world-space axis-aligned bounding box of what this scene transform covers.
    /// </summary>
    /// <value>
    /// The inclusive world-space AABB in absolute world units, or <see cref="Bounds.Empty"/> when
    /// the transform is invalid, hidden, or cannot currently be projected.
    /// </value>
    Bounds Bounds { get; }
}
