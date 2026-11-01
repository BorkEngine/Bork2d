using System.Globalization;
using System.Numerics;

namespace Bork2d.Engine.Mathematics;

/// <summary>
/// A world-space 2D ray: an origin and a normalized direction.
/// </summary>
/// <remarks>
/// <para>
/// The 2D adaptation of Unity 6000.6 <c>Ray2D</c>. The value is a mutable struct with settable
/// <see cref="Origin"/> and <see cref="Direction"/>. The constructor and the
/// <see cref="Direction"/> setter normalize the direction with
/// <c>Vector2Extensions.NormalizeOrZero</c>, so the stored direction is either unit length or
/// <see cref="Vector2.Zero"/> for a zero or effectively zero input. The <see cref="Origin"/> setter
/// stores the raw value.
/// </para>
/// <para>
/// <see cref="GetPoint(float)"/> walks along the direction; the distance is in world units, so the
/// result is <c>Origin + Direction * distance</c>. A negative distance walks backwards. The default
/// value is a ray at the origin with a zero direction, whose every point is the origin.
/// </para>
/// </remarks>
public struct Ray2D : IFormattable
{
    private const string DefaultFormat = "F2";

    private Vector2 _origin;
    private Vector2 _direction;

    /// <summary>
    /// Initializes a new instance of the <see cref="Ray2D"/> struct.
    /// </summary>
    /// <param name="origin">The world-space origin, in world units.</param>
    /// <param name="direction">
    /// The world-space direction; it is normalized, and a zero or effectively zero vector stays
    /// <see cref="Vector2.Zero"/>.
    /// </param>
    public Ray2D(Vector2 origin, Vector2 direction)
    {
        _origin = origin;
        _direction = Vector2.NormalizeOrZero(direction);
    }

    /// <summary>
    /// Gets or sets the world-space origin of the ray.
    /// </summary>
    /// <value>The origin in world units.</value>
    /// <remarks>The setter stores the raw value; it does not normalize or validate.</remarks>
    public Vector2 Origin
    {
        readonly get => _origin;
        set => _origin = value;
    }

    /// <summary>
    /// Gets or sets the world-space unit direction of the ray.
    /// </summary>
    /// <value>
    /// The direction, normalized to unit length, or <see cref="Vector2.Zero"/> when the assigned
    /// direction is zero or effectively zero.
    /// </value>
    /// <remarks>The setter normalizes with <c>Vector2Extensions.NormalizeOrZero</c>.</remarks>
    public Vector2 Direction
    {
        readonly get => _direction;
        set => _direction = Vector2.NormalizeOrZero(value);
    }

    /// <summary>
    /// Returns the point at a distance along the ray.
    /// </summary>
    /// <param name="distance">The distance from the origin, in world units.</param>
    /// <returns><c>Origin + Direction * distance</c>; a zero direction returns the origin.</returns>
    public readonly Vector2 GetPoint(float distance) => _origin + (_direction * distance);

    /// <summary>
    /// Returns the ray formatted as <c>Origin: (x, y), Dir: (x, y)</c> with <c>F2</c> components and
    /// invariant culture.
    /// </summary>
    /// <returns>The formatted ray.</returns>
    public override readonly string ToString() => ToString(null, null);

    /// <summary>
    /// Returns the ray formatted as <c>Origin: (x, y), Dir: (x, y)</c>.
    /// </summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F2</c>.</param>
    /// <returns>The formatted ray with invariant culture.</returns>
    public readonly string ToString(string? format) => ToString(format, null);

    /// <summary>
    /// Returns the ray formatted as <c>Origin: (x, y), Dir: (x, y)</c>.
    /// </summary>
    /// <param name="format">A numeric format string; <see langword="null"/> or empty means <c>F2</c>.</param>
    /// <param name="formatProvider">
    /// The provider for each component; <see langword="null"/> means invariant culture.
    /// </param>
    /// <returns>The formatted ray.</returns>
    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        format = string.IsNullOrEmpty(format) ? DefaultFormat : format;
        formatProvider ??= CultureInfo.InvariantCulture;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Origin: ({0}, {1}), Dir: ({2}, {3})",
            _origin.X.ToString(format, formatProvider),
            _origin.Y.ToString(format, formatProvider),
            _direction.X.ToString(format, formatProvider),
            _direction.Y.ToString(format, formatProvider));
    }
}
