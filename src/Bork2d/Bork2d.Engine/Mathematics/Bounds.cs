using System.Globalization;
using System.Numerics;

namespace Bork2d.Engine.Mathematics;

/// <summary>
/// A world-space, axis-aligned 2D bounding box.
/// </summary>
/// <remarks>
/// <para>
/// The 2D adaptation of Unity 6000.6 <c>Bounds</c>. The box lives in world space: +X right, +Y up,
/// in absolute world units. <see cref="Min"/> and <see cref="Max"/> are inclusive, so a point
/// exactly on an edge is contained and two boxes that only touch at an edge or corner intersect.
/// Rotated content is represented by the axis-aligned box that contains it, which is larger than
/// the content itself.
/// </para>
/// <para>
/// The value is a mutable, Unity-style struct with settable <see cref="Center"/>, <see cref="Size"/>,
/// <see cref="Extents"/>, <see cref="Min"/>, and <see cref="Max"/>, plus the mutating
/// <see cref="Encapsulate(Vector2)"/>, <see cref="Expand(float)"/>, and
/// <see cref="SetMinMax(Vector2, Vector2)"/> helpers. Construct it from a center and a full size, or
/// from two corners with <see cref="FromMinMax(Vector2, Vector2)"/>.
/// </para>
/// <para>
/// Unlike Unity, a box without positive area is <see cref="IsEmpty"/>: the default value, a
/// zero-area box, an inverted box (a minimum beyond the maximum on either axis), and any box with a
/// non-finite center or extent are empty. <see cref="Empty"/> is the default value and means "no
/// bounds" or "unknown bounds"; the engine treats empty bounds as "never cull" rather than
/// "outside". The query methods are empty-safe: <see cref="Contains(Vector2)"/>,
/// <see cref="Intersects(in Bounds)"/>, and <see cref="IntersectRay(in Ray2D)"/> return
/// <see langword="false"/> for an empty operand, <see cref="ClosestPoint"/> returns
/// <see cref="Vector2.Zero"/>, and <see cref="SqrDistance"/> returns
/// <see cref="float.PositiveInfinity"/>.
/// </para>
/// <para>
/// The <see cref="Center"/>, <see cref="Size"/>, and <see cref="Extents"/> getters return
/// <see cref="Vector2.Zero"/> for an empty box, while their setters store the raw value. The
/// <see cref="Min"/> and <see cref="Max"/> getters return the raw stored edges even when the box is
/// empty, so <see cref="Encapsulate(Vector2)"/>, <see cref="Expand(float)"/>, and
/// <see cref="SetMinMax(Vector2, Vector2)"/> operate on the raw box and can grow or replace an empty
/// one.
/// </para>
/// </remarks>
public struct Bounds : IEquatable<Bounds>, IFormattable
{
    private const string DefaultFormat = "F2";

    private Vector2 _center;
    private Vector2 _extents;

    /// <summary>
    /// Initializes a new instance of the <see cref="Bounds"/> struct.
    /// </summary>
    /// <param name="center">The center of the box, in world units.</param>
    /// <param name="size">The full width and height of the box, in world units.</param>
    public Bounds(Vector2 center, Vector2 size)
    {
        _center = center;
        _extents = size * 0.5f;
    }

    /// <summary>
    /// Gets the empty bounds sentinel, meaning "no bounds" or "unknown bounds".
    /// </summary>
    /// <remarks>The default <see cref="Bounds"/> value equals <see cref="Empty"/>.</remarks>
    public static Bounds Empty => default;

    /// <summary>
    /// Gets a value indicating whether this box has no positive area.
    /// </summary>
    /// <remarks>
    /// A box is empty when its stored center or extents are non-finite, or when either extent is
    /// zero or negative. Empty bounds never contain a point, never intersect another box, and never
    /// intersect a ray, so callers that treat bounds as a culling region must treat an empty box as
    /// "unknown" rather than "outside". Non-finite values are empty rather than an unbounded region,
    /// so a degenerate or non-finite world matrix can never turn into an accidental cull.
    /// </remarks>
    public readonly bool IsEmpty => !(float.IsFinite(_center.X)
        && float.IsFinite(_center.Y)
        && float.IsFinite(_extents.X)
        && float.IsFinite(_extents.Y)
        && _extents.X > 0f
        && _extents.Y > 0f);

    /// <summary>
    /// Gets or sets the center of the box.
    /// </summary>
    /// <value>The center in world units, or <see cref="Vector2.Zero"/> when the box is empty.</value>
    /// <remarks>The setter stores the raw value; the getter reports zero for an empty box.</remarks>
    public Vector2 Center
    {
        readonly get => IsEmpty ? Vector2.Zero : _center;
        set => _center = value;
    }

    /// <summary>
    /// Gets or sets the full width and height of the box.
    /// </summary>
    /// <value>The size in world units, or <see cref="Vector2.Zero"/> when the box is empty.</value>
    /// <remarks>
    /// The setter stores the raw value as half extents; a negative component makes the box empty.
    /// </remarks>
    public Vector2 Size
    {
        readonly get => IsEmpty ? Vector2.Zero : _extents * 2f;
        set => _extents = value * 0.5f;
    }

    /// <summary>
    /// Gets or sets the half width and half height of the box.
    /// </summary>
    /// <value>The extents in world units, or <see cref="Vector2.Zero"/> when the box is empty.</value>
    /// <remarks>The setter stores the raw value; a zero or negative component makes the box empty.</remarks>
    public Vector2 Extents
    {
        readonly get => IsEmpty ? Vector2.Zero : _extents;
        set => _extents = value;
    }

    /// <summary>
    /// Gets or sets the inclusive minimum corner of the box.
    /// </summary>
    /// <value>The raw stored minimum corner in world units.</value>
    /// <remarks>
    /// The getter returns the raw stored edge even for an empty box so <see cref="Encapsulate(Vector2)"/>
    /// and <see cref="Expand(float)"/> can operate on it. The setter calls
    /// <see cref="SetMinMax(Vector2, Vector2)"/> with the current <see cref="Max"/>.
    /// </remarks>
    public Vector2 Min
    {
        readonly get => _center - _extents;
        set => SetMinMax(value, Max);
    }

    /// <summary>
    /// Gets or sets the inclusive maximum corner of the box.
    /// </summary>
    /// <value>The raw stored maximum corner in world units.</value>
    /// <remarks>
    /// The getter returns the raw stored edge even for an empty box so <see cref="Encapsulate(Vector2)"/>
    /// and <see cref="Expand(float)"/> can operate on it. The setter calls
    /// <see cref="SetMinMax(Vector2, Vector2)"/> with the current <see cref="Min"/>.
    /// </remarks>
    public Vector2 Max
    {
        readonly get => _center + _extents;
        set => SetMinMax(Min, value);
    }

    /// <summary>
    /// Creates a box from two corners.
    /// </summary>
    /// <param name="min">The inclusive minimum corner, in world units.</param>
    /// <param name="max">The inclusive maximum corner, in world units.</param>
    /// <returns>The box whose raw edges are <paramref name="min"/> and <paramref name="max"/>.</returns>
    /// <remarks>A box whose minimum exceeds its maximum on either axis is empty.</remarks>
    public static Bounds FromMinMax(Vector2 min, Vector2 max)
    {
        var bounds = default(Bounds);
        bounds.SetMinMax(min, max);
        return bounds;
    }

    /// <summary>
    /// Returns the point inside the box that is closest to <paramref name="point"/>.
    /// </summary>
    /// <param name="point">The world-space point, in world units.</param>
    /// <returns>
    /// <paramref name="point"/> when it is contained, the clamped edge or corner otherwise, or
    /// <see cref="Vector2.Zero"/> when the box is empty.
    /// </returns>
    public readonly Vector2 ClosestPoint(Vector2 point)
    {
        if (IsEmpty)
        {
            return Vector2.Zero;
        }

        Vector2 min = Min;
        Vector2 max = Max;
        return new Vector2(
            Math.Clamp(point.X, min.X, max.X),
            Math.Clamp(point.Y, min.Y, max.Y));
    }

    /// <summary>
    /// Returns whether the box contains a world-space point.
    /// </summary>
    /// <remarks>The test is inclusive: a point on the box edge is contained.</remarks>
    /// <param name="point">The world-space point, in world units.</param>
    /// <returns><see langword="false"/> when the box is empty.</returns>
    public readonly bool Contains(Vector2 point)
        => !IsEmpty
            && point.X >= Min.X
            && point.X <= Max.X
            && point.Y >= Min.Y
            && point.Y <= Max.Y;

    /// <summary>
    /// Grows the box to include a world-space point.
    /// </summary>
    /// <remarks>
    /// The helper operates on the raw stored box: from <see cref="Empty"/> it grows from the origin,
    /// and from a zero-size box at a point it grows from that point. The result is still empty when
    /// the point only touches a zero-area box.
    /// </remarks>
    /// <param name="point">The world-space point, in world units.</param>
    public void Encapsulate(Vector2 point)
        => SetMinMax(Vector2.Min(Min, point), Vector2.Max(Max, point));

    /// <summary>
    /// Grows the box to include another box.
    /// </summary>
    /// <remarks>
    /// Both the receiver and <paramref name="bounds"/> contribute their raw stored edges, so
    /// encapsulating <see cref="Empty"/> grows from the origin instead of being a no-op. Callers that
    /// treat empty bounds as "unknown" must check <see cref="IsEmpty"/> first.
    /// </remarks>
    /// <param name="bounds">The box to include.</param>
    public void Encapsulate(in Bounds bounds)
    {
        Encapsulate(bounds.Min);
        Encapsulate(bounds.Max);
    }

    /// <summary>
    /// Expands the box by the same total amount on both axes.
    /// </summary>
    /// <remarks>
    /// The box grows by <paramref name="amount"/> on every axis, so each side moves by half the
    /// amount. A negative amount shrinks the box and can make it empty. The helper operates on the
    /// raw stored box, so expanding <see cref="Empty"/> produces a box centered on the origin.
    /// </remarks>
    /// <param name="amount">The total amount to add to the width and height, in world units.</param>
    public void Expand(float amount)
    {
        float half = amount * 0.5f;
        _extents += new Vector2(half, half);
    }

    /// <summary>
    /// Expands the box by a per-axis total amount.
    /// </summary>
    /// <remarks>
    /// Each axis grows by the matching <paramref name="amount"/> component, so each side moves by
    /// half of it. Negative components shrink the box and can make it empty. The helper operates on
    /// the raw stored box, so expanding <see cref="Empty"/> produces a box centered on the origin.
    /// </remarks>
    /// <param name="amount">The total amount to add to the width and height, in world units.</param>
    public void Expand(Vector2 amount) => _extents += amount * 0.5f;

    /// <summary>
    /// Returns whether two boxes overlap, including when they only touch at an edge or corner.
    /// </summary>
    /// <param name="other">The other world-space box.</param>
    /// <returns><see langword="false"/> when either box is empty.</returns>
    public readonly bool Intersects(in Bounds other)
        => !IsEmpty
            && !other.IsEmpty
            && Min.X <= other.Max.X
            && Max.X >= other.Min.X
            && Min.Y <= other.Max.Y
            && Max.Y >= other.Min.Y;

    /// <summary>
    /// Sets the box from two corners.
    /// </summary>
    /// <remarks>
    /// The center becomes the midpoint and the extents half the difference, exactly as in Unity. A
    /// minimum greater than the maximum on either axis produces an empty box.
    /// </remarks>
    /// <param name="min">The inclusive minimum corner, in world units.</param>
    /// <param name="max">The inclusive maximum corner, in world units.</param>
    public void SetMinMax(Vector2 min, Vector2 max)
    {
        _extents = (max - min) * 0.5f;
        _center = min + _extents;
    }

    /// <summary>
    /// Returns the squared distance from <paramref name="point"/> to the closest point of the box.
    /// </summary>
    /// <param name="point">The world-space point, in world units.</param>
    /// <returns>
    /// <c>0</c> for a contained point, a positive squared distance outside the box, or
    /// <see cref="float.PositiveInfinity"/> when the box is empty.
    /// </returns>
    public readonly float SqrDistance(Vector2 point)
    {
        if (IsEmpty)
        {
            return float.PositiveInfinity;
        }

        Vector2 offset = point - ClosestPoint(point);
        return Vector2.Dot(offset, offset);
    }

    /// <summary>
    /// Returns whether a ray intersects the box.
    /// </summary>
    /// <param name="ray">The world-space ray.</param>
    /// <returns><see langword="false"/> when the box is empty or the ray misses it.</returns>
    public readonly bool IntersectRay(in Ray2D ray) => IntersectRay(in ray, out _);

    /// <summary>
    /// Returns whether a ray intersects the box and the world-space distance to the first hit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test is a 2D slab test in world units. A ray origin inside the box reports
    /// <c>0</c>, and a ray pointing away from the box misses. A zero-direction ray reports
    /// <c>0</c> only when its origin is contained, so a ray that starts on an edge counts as a hit.
    /// </para>
    /// <para>
    /// <see langword="false"/> is returned when the box is empty; <paramref name="distance"/> is
    /// then <c>0</c>.
    /// </para>
    /// </remarks>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="distance">
    /// The world-space distance from the ray origin to the first intersection, or <c>0</c> when the
    /// method returns <see langword="false"/>.
    /// </param>
    /// <returns><see langword="true"/> when the ray intersects the box.</returns>
    public readonly bool IntersectRay(in Ray2D ray, out float distance)
    {
        distance = 0f;
        if (IsEmpty)
        {
            return false;
        }

        Vector2 origin = ray.Origin;
        Vector2 direction = ray.Direction;
        if (Contains(origin))
        {
            return true;
        }

        if (direction.X == 0f && direction.Y == 0f)
        {
            return false;
        }

        Vector2 min = Min;
        Vector2 max = Max;
        float near = float.NegativeInfinity;
        float far = float.PositiveInfinity;
        if (!IntersectSlab(origin.X, direction.X, min.X, max.X, ref near, ref far)
            || !IntersectSlab(origin.Y, direction.Y, min.Y, max.Y, ref near, ref far)
            || far < 0f)
        {
            return false;
        }

        distance = MathF.Max(near, 0f);
        return true;
    }

    /// <summary>
    /// Splits the box into its raw stored minimum and maximum corners.
    /// </summary>
    /// <param name="min">The raw minimum corner, in world units.</param>
    /// <param name="max">The raw maximum corner, in world units.</param>
    public readonly void Deconstruct(out Vector2 min, out Vector2 max)
    {
        min = Min;
        max = Max;
    }

    /// <summary>
    /// Returns the box formatted as <c>Center: (x, y), Extents: (x, y)</c> with <c>F2</c>
    /// components and invariant culture.
    /// </summary>
    /// <returns>The formatted box.</returns>
    public override readonly string ToString() => ToString(null, null);

    /// <summary>
    /// Returns the box formatted as <c>Center: (x, y), Extents: (x, y)</c>.
    /// </summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F2</c>.</param>
    /// <returns>The formatted box with invariant culture.</returns>
    public readonly string ToString(string? format) => ToString(format, null);

    /// <summary>
    /// Returns the box formatted as <c>Center: (x, y), Extents: (x, y)</c>.
    /// </summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F2</c>.</param>
    /// <param name="formatProvider">
    /// The provider for each component; <see langword="null"/> means invariant culture.
    /// </param>
    /// <returns>The formatted box.</returns>
    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        format = string.IsNullOrEmpty(format) ? DefaultFormat : format;
        formatProvider ??= CultureInfo.InvariantCulture;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Center: ({0}, {1}), Extents: ({2}, {3})",
            _center.X.ToString(format, formatProvider),
            _center.Y.ToString(format, formatProvider),
            _extents.X.ToString(format, formatProvider),
            _extents.Y.ToString(format, formatProvider));
    }

    /// <summary>
    /// Determines whether this box has exactly the same raw center and extents as another box.
    /// </summary>
    /// <param name="other">The box to compare.</param>
    /// <returns><see langword="true"/> when the raw center and extents are exactly equal.</returns>
    public readonly bool Equals(Bounds other)
        => _center == other._center && _extents == other._extents;

    /// <summary>
    /// Determines whether this box equals another object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="obj"/> is a box with exactly equal raw center and
    /// extents.
    /// </returns>
    public override readonly bool Equals(object? obj) => obj is Bounds other && Equals(other);

    /// <summary>
    /// Returns a hash code consistent with exact raw center and extents equality.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode()
        => _center.GetHashCode() ^ (_extents.GetHashCode() << 2);

    /// <summary>Determines whether two boxes have exactly equal raw center and extents.</summary>
    /// <param name="left">The left box.</param>
    /// <param name="right">The right box.</param>
    /// <returns><see langword="true"/> when both boxes are exactly equal.</returns>
    public static bool operator ==(Bounds left, Bounds right) => left.Equals(right);

    /// <summary>Determines whether two boxes differ in their raw center or extents.</summary>
    /// <param name="left">The left box.</param>
    /// <param name="right">The right box.</param>
    /// <returns><see langword="true"/> when any raw component differs exactly.</returns>
    public static bool operator !=(Bounds left, Bounds right) => !left.Equals(right);

    private static bool IntersectSlab(
        float origin,
        float direction,
        float min,
        float max,
        ref float near,
        ref float far)
    {
        if (direction == 0f)
        {
            return origin >= min && origin <= max;
        }

        float first = (min - origin) / direction;
        float second = (max - origin) / direction;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }
}
