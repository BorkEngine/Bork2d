using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Scene;

/// <summary>
/// A line renderable in the scene graph: a transform plus an open or closed polyline and
/// line-specific draw state.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// Line renderables are scene entities. They do not implement <see cref="IDisposable"/> and have no
/// liveness flag: detach with <see cref="ITransform.Parent"/> = <see langword="null"/> or clear the
/// owning parent to remove one, and re-attach the same instance later. Hide a line by assigning
/// <see cref="Points"/> = <see langword="null"/> or an empty list.
/// </para>
/// <para>
/// The engine expands the polyline on the CPU into local-space triangles and caches the immutable
/// result. Points, <see cref="Width"/>, and <see cref="Closed"/> rebuild the cached geometry; color,
/// anti-aliasing, shader, layer, z-index, and transform changes do not. The world transform is
/// applied by the renderer, so moving a line never rebuilds it.
/// </para>
/// <para>
/// Geometry validation is conservative and happens without throwing: an open line needs at least
/// two distinct points after duplicate consecutive points are skipped, a closed line needs at least
/// three, every point must be finite, and <see cref="Width"/> must be finite and greater than zero.
/// A line that fails validation is captured as not drawable and the renderer later skips it with a
/// one-time diagnostic. A miter join beyond the limit (<c>4</c> times the half-width) falls back to
/// a bevel for that corner.
/// </para>
/// <para>
/// <see cref="IBoundedTransform.Bounds"/> is the world-space AABB of the expanded geometry, including
/// half the width and every miter or bevel extent. It returns <see cref="Bounds.Empty"/> for a line
/// with no drawable geometry or a non-finite transform and never throws for those cases.
/// </para>
/// <para>
/// No member requires a particular thread. Property access and
/// <see cref="IRenderable{TLayer}.SetUniform(string, float)"/> use the renderable's per-object lock,
/// and capture reads all fields under one lock acquisition; the engine's structural lock is never
/// held across a wait.
/// </para>
/// <para>
/// The shared draw state — <see cref="IRenderable{TLayer}.Shader"/>,
/// <see cref="IRenderable{TLayer}.Color"/>, <see cref="IRenderable{TLayer}.Layer"/>,
/// <see cref="IRenderable{TLayer}.ZIndex"/>, the instance uniform overloads, and
/// <see cref="IRenderable{TLayer}.ResetUniforms"/> — is declared by
/// <see cref="IRenderable{TLayer}"/>.
/// </para>
/// </remarks>
public interface ILineRender<TLayer> : IRenderable<TLayer> where TLayer : struct, Enum
{
    /// <summary>
    /// Gets or sets the polyline points in local world units; <see langword="null"/> or an empty
    /// list draws nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setter copies the values from the supplied list, so mutating the caller's array or list
    /// afterwards does not change this renderable or a published snapshot. The getter returns a
    /// read-only view of the stored copy; it allocates nothing per get and does not expose a mutable
    /// array. Assigning a new list rebuilds the cached geometry; a list with the same values still
    /// counts as a new assignment.
    /// </para>
    /// <para>
    /// An open line requires at least two distinct points and a closed line at least three.
    /// Duplicate consecutive points are skipped, and a closed line drops a final point equal to the
    /// first before joining the last point to the first. A non-finite point makes the line
    /// non-drawable without throwing.
    /// </para>
    /// </remarks>
    IReadOnlyList<Vector2> Points { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last point joins the first; the default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A closed line has no end caps. Changing this value rebuilds the cached geometry; an
    /// out-of-range value is stored as-is and only affects whether the geometry is drawable.
    /// </remarks>
    bool Closed { get; set; }

    /// <summary>
    /// Gets or sets the full line width in local world units; the default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The setter stores the value without validating or throwing. A non-finite or non-positive
    /// width makes the line non-drawable at capture. Changing the width rebuilds the cached
    /// geometry.
    /// </remarks>
    float Width { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the renderer softens the line edges; the default is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Anti-aliasing is a fragment-stage coverage term, not extra geometry, so changing it never
    /// rebuilds the cached triangles. The engine stores and captures the flag; the line renderer
    /// consumes it.
    /// </remarks>
    bool AntiAlias { get; set; }
}
