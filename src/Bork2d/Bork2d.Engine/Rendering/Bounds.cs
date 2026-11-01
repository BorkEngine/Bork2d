using System.Numerics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A world-space axis-aligned bounding box (AABB).
/// </summary>
/// <remarks>
/// <para>
/// The box lives in world space: +X right, +Y up, in absolute world units. <see cref="Min"/> and
/// <see cref="Max"/> are inclusive, so a point exactly on an edge is contained and two boxes that
/// only touch at an edge or corner intersect. Rotated content is represented by the axis-aligned
/// box that contains it, which is larger than the content itself.
/// </para>
/// <para>
/// A value has no positive area exactly when <see cref="IsEmpty"/> is <see langword="true"/>: the
/// default value, an inverted box whose minimum exceeds its maximum on either axis, and any box
/// with a non-finite limit are empty. <see cref="Empty"/> is the default value and means "no
/// bounds" or "unknown bounds". <see cref="Contains(Vector2)"/> and <see cref="Intersects(in Bounds)"/>
/// return <see langword="false"/> for an empty operand, and <see cref="Center"/> and
/// <see cref="Size"/> return <see cref="Vector2.Zero"/> for an empty box.
/// </para>
/// </remarks>
/// <param name="Min">The inclusive minimum corner, in world units.</param>
/// <param name="Max">The inclusive maximum corner, in world units.</param>
public readonly record struct Bounds(Vector2 Min, Vector2 Max)
{
    /// <summary>
    /// Gets the empty bounds sentinel, meaning "no bounds" or "unknown bounds".
    /// </summary>
    /// <remarks>The default <see cref="Bounds"/> value equals <see cref="Empty"/>.</remarks>
    public static Bounds Empty => default;

    /// <summary>
    /// Gets a value indicating whether this box has no positive area.
    /// </summary>
    /// <remarks>
    /// Empty bounds never contain a point and never intersect another box, so callers that treat
    /// bounds as a culling region must treat an empty box as "unknown" rather than "outside".
    /// Non-finite limits are empty rather than an unbounded region, so a degenerate or
    /// non-finite world matrix can never turn into an accidental cull.
    /// </remarks>
    public bool IsEmpty => !(float.IsFinite(Min.X)
        && float.IsFinite(Min.Y)
        && float.IsFinite(Max.X)
        && float.IsFinite(Max.Y)
        && Min.X < Max.X
        && Min.Y < Max.Y);

    /// <summary>
    /// Gets the center of the box, or <see cref="Vector2.Zero"/> when the box is empty.
    /// </summary>
    public Vector2 Center => IsEmpty ? Vector2.Zero : (Min + Max) * 0.5f;

    /// <summary>
    /// Gets the width and height of the box, or <see cref="Vector2.Zero"/> when the box is empty.
    /// </summary>
    public Vector2 Size => IsEmpty ? Vector2.Zero : Max - Min;

    /// <summary>
    /// Returns whether the box contains a world-space point.
    /// </summary>
    /// <remarks>The test is inclusive: a point on the box edge is contained.</remarks>
    /// <param name="point">The world-space point, in world units.</param>
    /// <returns><see langword="false"/> when the box is empty.</returns>
    public bool Contains(Vector2 point)
        => !IsEmpty
            && point.X >= Min.X
            && point.X <= Max.X
            && point.Y >= Min.Y
            && point.Y <= Max.Y;

    /// <summary>
    /// Returns whether two boxes overlap, including when they only touch at an edge or corner.
    /// </summary>
    /// <param name="other">The other world-space box.</param>
    /// <returns><see langword="false"/> when either box is empty.</returns>
    public bool Intersects(in Bounds other)
        => !IsEmpty
            && !other.IsEmpty
            && Min.X <= other.Max.X
            && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y
            && Max.Y >= other.Min.Y;
}
