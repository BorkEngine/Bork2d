using System.Numerics;

namespace Bork2d.Engine.Mathematics;

/// <summary>
/// Unity 6000.6 <see cref="Vector2"/> helpers implemented as C# extension members.
/// </summary>
/// <remarks>
/// <para>
/// The engine uses <see cref="Vector2"/> as its vector type, so these members extend that type
/// instead of introducing a second one. Import <c>Bork2d.Engine.Mathematics</c> to make them
/// visible; the call syntax is then <c>Vector2.Up</c>, <c>value.Magnitude</c>, or
/// <c>Vector2.Angle(from, to)</c>.
/// </para>
/// <para>
/// Members that <see cref="Vector2"/> already defines keep their .NET semantics: <c>Lerp</c> does
/// not clamp, static <c>Normalize</c> returns NaN components for a zero vector, <c>==</c> compares
/// exactly, and <c>ToString</c>, <c>Equals</c>, and <c>GetHashCode</c> are unchanged. Unity behavior
/// is available under distinct names: <c>LerpClamped</c>, <c>NormalizeOrZero</c>, and
/// <c>ApproximatelyEquals</c>. The static <c>ApproximatelyEquals(Vector2, Vector2)</c> lives in
/// <see cref="Vector2ApproximatelyExtensions"/> because C# extension blocks lower a same-named
/// static and instance member to colliding static methods.
/// </para>
/// </remarks>
public static class Vector2Extensions
{
    private const float ZeroMagnitudeEpsilon = 1e-5f;
    private const float AngleDenominatorEpsilon = 1e-30f;
    private const float ApproximatelyEqualEpsilon = 1e-10f;
    private const float DegreesPerRadian = 180f / MathF.PI;

    extension(Vector2 v)
    {
        /// <summary>Gets the length of the vector.</summary>
        public float Magnitude => v.Length();

        /// <summary>Gets the squared length of the vector.</summary>
        public float SqrMagnitude => v.LengthSquared();

        /// <summary>
        /// Gets the vector scaled to unit length, or <see cref="Vector2.Zero"/> when its length is at
        /// most <c>1e-5</c>.
        /// </summary>
        public Vector2 Normalized => NormalizeOrZero(v);

        /// <summary>Gets the unit vector that points up: <c>(0, 1)</c>.</summary>
        public static Vector2 Up => new(0f, 1f);

        /// <summary>Gets the unit vector that points down: <c>(0, -1)</c>.</summary>
        public static Vector2 Down => new(0f, -1f);

        /// <summary>Gets the unit vector that points left: <c>(-1, 0)</c>.</summary>
        public static Vector2 Left => new(-1f, 0f);

        /// <summary>Gets the unit vector that points right: <c>(1, 0)</c>.</summary>
        public static Vector2 Right => new(1f, 0f);

        /// <summary>
        /// Returns the unsigned angle in degrees between <paramref name="from"/> and
        /// <paramref name="to"/>.
        /// </summary>
        /// <param name="from">The vector the angle is measured from.</param>
        /// <param name="to">The vector the angle is measured to.</param>
        /// <returns>
        /// The angle in <c>[0, 180]</c> degrees, or <c>0</c> when either vector is effectively zero.
        /// </returns>
        public static float Angle(Vector2 from, Vector2 to)
        {
            float denominator = from.LengthSquared() * to.LengthSquared();
            if (denominator < AngleDenominatorEpsilon)
            {
                return 0f;
            }

            denominator = MathF.Sqrt(denominator);
            float dot = Math.Clamp(Vector2.Dot(from, to) / denominator, -1f, 1f);
            return MathF.Acos(dot) * DegreesPerRadian;
        }

        /// <summary>
        /// Returns the signed angle in degrees between <paramref name="from"/> and
        /// <paramref name="to"/>, counter-clockwise positive.
        /// </summary>
        /// <param name="from">The vector the angle is measured from.</param>
        /// <param name="to">The vector the angle is measured to.</param>
        /// <returns>
        /// The angle in <c>[-180, 180]</c> degrees. The sign follows the cross product, which is
        /// <c>+1</c> for a zero cross product. A zero vector yields <c>+0</c>.
        /// </returns>
        public static float SignedAngle(Vector2 from, Vector2 to)
        {
            float sign = ((from.X * to.Y) - (from.Y * to.X)) >= 0f ? 1f : -1f;
            return Angle(from, to) * sign;
        }

        /// <summary>
        /// Returns a copy of <paramref name="vector"/> with its length limited to
        /// <paramref name="maxLength"/>.
        /// </summary>
        /// <param name="vector">The vector to limit.</param>
        /// <param name="maxLength">The maximum length of the result.</param>
        /// <returns>
        /// The original vector when its length is at most <paramref name="maxLength"/>, otherwise
        /// the vector scaled to <paramref name="maxLength"/>; a <paramref name="maxLength"/> of
        /// <c>0</c> therefore yields a zero vector for a non-zero input.
        /// </returns>
        public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
        {
            float squareMagnitude = vector.LengthSquared();
            if (squareMagnitude > maxLength * maxLength)
            {
                float magnitude = MathF.Sqrt(squareMagnitude);
                float normalizedX = vector.X / magnitude;
                float normalizedY = vector.Y / magnitude;
                return new Vector2(normalizedX * maxLength, normalizedY * maxLength);
            }

            return vector;
        }

        /// <summary>
        /// Moves <paramref name="current"/> towards <paramref name="target"/> by at most
        /// <paramref name="maxDistanceDelta"/>.
        /// </summary>
        /// <param name="current">The position to move from.</param>
        /// <param name="target">The position to move towards.</param>
        /// <param name="maxDistanceDelta">
        /// The maximum distance to move. A positive value stops at the target; a negative value
        /// moves away from it.
        /// </param>
        /// <returns>The new position.</returns>
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
        {
            float toVectorX = target.X - current.X;
            float toVectorY = target.Y - current.Y;
            float squareDistance = (toVectorX * toVectorX) + (toVectorY * toVectorY);

            if (squareDistance == 0f
                || (maxDistanceDelta >= 0f && squareDistance <= maxDistanceDelta * maxDistanceDelta))
            {
                return target;
            }

            float distance = MathF.Sqrt(squareDistance);
            return new Vector2(
                current.X + ((toVectorX / distance) * maxDistanceDelta),
                current.Y + ((toVectorY / distance) * maxDistanceDelta));
        }

        /// <summary>
        /// Returns <paramref name="inDirection"/> rotated 90 degrees counter-clockwise in the
        /// +Y-up convention.
        /// </summary>
        /// <param name="inDirection">The direction to rotate.</param>
        /// <returns><c>(-inDirection.Y, inDirection.X)</c>.</returns>
        public static Vector2 Perpendicular(Vector2 inDirection)
            => new(-inDirection.Y, inDirection.X);

        /// <summary>Returns the component-wise product of two vectors.</summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        /// <returns><c>(a.X * b.X, a.Y * b.Y)</c>.</returns>
        public static Vector2 Scale(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);

        /// <summary>
        /// Interpolates between <paramref name="a"/> and <paramref name="b"/> without clamping
        /// <paramref name="t"/>.
        /// </summary>
        /// <param name="a">The start vector, returned when <paramref name="t"/> is <c>0</c>.</param>
        /// <param name="b">The end vector, returned when <paramref name="t"/> is <c>1</c>.</param>
        /// <param name="t">The interpolation ratio; values outside <c>[0, 1]</c> extrapolate.</param>
        /// <returns>The interpolated vector.</returns>
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
            => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        /// <summary>
        /// Smooths <paramref name="current"/> towards <paramref name="target"/> over time, following
        /// Unity's <c>SmoothDamp</c> algorithm.
        /// </summary>
        /// <param name="current">The position to move from.</param>
        /// <param name="target">The position to move towards.</param>
        /// <param name="currentVelocity">
        /// The current velocity, updated in place with the velocity of the returned position.
        /// </param>
        /// <param name="smoothTime">
        /// The approximate time to reach the target; values below <c>0.0001</c> are clamped.
        /// </param>
        /// <param name="maxSpeed">The maximum speed; <see cref="float.PositiveInfinity"/> disables the clamp.</param>
        /// <param name="deltaTime">The elapsed time since the previous call.</param>
        /// <returns>The new position, which never overshoots the target.</returns>
        public static Vector2 SmoothDamp(
            Vector2 current,
            Vector2 target,
            ref Vector2 currentVelocity,
            float smoothTime,
            float maxSpeed,
            float deltaTime)
        {
            smoothTime = MathF.Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;

            float x = omega * deltaTime;
            float exp = 1f / (1f + x + (0.48f * x * x) + (0.235f * x * x * x));

            float changeX = current.X - target.X;
            float changeY = current.Y - target.Y;

            float maxChange = maxSpeed * smoothTime;
            float maxChangeSquared = maxChange * maxChange;
            float squareDistance = (changeX * changeX) + (changeY * changeY);
            if (squareDistance > maxChangeSquared)
            {
                float magnitude = MathF.Sqrt(squareDistance);
                changeX = changeX / magnitude * maxChange;
                changeY = changeY / magnitude * maxChange;
            }

            float targetX = current.X - changeX;
            float targetY = current.Y - changeY;

            float tempX = (currentVelocity.X + (omega * changeX)) * deltaTime;
            float tempY = (currentVelocity.Y + (omega * changeY)) * deltaTime;

            currentVelocity.X = (currentVelocity.X - (omega * tempX)) * exp;
            currentVelocity.Y = (currentVelocity.Y - (omega * tempY)) * exp;

            float outputX = targetX + ((changeX + tempX) * exp);
            float outputY = targetY + ((changeY + tempY) * exp);

            float originalMinusCurrentX = target.X - current.X;
            float originalMinusCurrentY = target.Y - current.Y;
            float outMinusOriginalX = outputX - target.X;
            float outMinusOriginalY = outputY - target.Y;

            if ((originalMinusCurrentX * outMinusOriginalX)
                + (originalMinusCurrentY * outMinusOriginalY) > 0f)
            {
                outputX = target.X;
                outputY = target.Y;

                currentVelocity.X = (outputX - target.X) / deltaTime;
                currentVelocity.Y = (outputY - target.Y) / deltaTime;
            }

            return new Vector2(outputX, outputY);
        }

        /// <summary>
        /// Interpolates between <paramref name="a"/> and <paramref name="b"/> with Unity's clamping
        /// <c>Lerp</c>, which limits <paramref name="t"/> to <c>[0, 1]</c>.
        /// </summary>
        /// <param name="a">The start vector, returned when <paramref name="t"/> is at most <c>0</c>.</param>
        /// <param name="b">The end vector, returned when <paramref name="t"/> is at least <c>1</c>.</param>
        /// <param name="t">The interpolation ratio; values outside <c>[0, 1]</c> are clamped.</param>
        /// <returns>The interpolated vector.</returns>
        public static Vector2 LerpClamped(Vector2 a, Vector2 b, float t)
            => LerpUnclamped(a, b, Math.Clamp(t, 0f, 1f));

        /// <summary>
        /// Returns <paramref name="value"/> scaled to unit length, or <see cref="Vector2.Zero"/>
        /// when its length is at most <c>1e-5</c>.
        /// </summary>
        /// <param name="value">The vector to normalize.</param>
        /// <returns>The normalized vector, or a zero vector for an effectively zero input.</returns>
        public static Vector2 NormalizeOrZero(Vector2 value)
        {
            float magnitude = value.Length();
            if (magnitude <= ZeroMagnitudeEpsilon)
            {
                return Vector2.Zero;
            }

            return value / magnitude;
        }

        /// <summary>
        /// Determines whether this vector is equal to <paramref name="other"/> within Unity's
        /// approximate comparison.
        /// </summary>
        /// <param name="other">The vector to compare with.</param>
        /// <returns>
        /// <see langword="true"/> when the squared difference is below <c>1e-10</c>; otherwise
        /// <see langword="false"/>.
        /// </returns>
        public bool ApproximatelyEquals(Vector2 other)
            => (v - other).LengthSquared() < ApproximatelyEqualEpsilon;
    }

    extension(ref Vector2 v)
    {
        /// <summary>Sets both components of the vector.</summary>
        /// <param name="newX">The new <c>X</c> component.</param>
        /// <param name="newY">The new <c>Y</c> component.</param>
        public void Set(float newX, float newY)
        {
            v.X = newX;
            v.Y = newY;
        }

        /// <summary>Multiplies both components of the vector by the components of a scale vector.</summary>
        /// <param name="scale">The component-wise scale.</param>
        public void Scale(Vector2 scale)
        {
            v.X *= scale.X;
            v.Y *= scale.Y;
        }

        /// <summary>
        /// Scales the vector to unit length in place, or sets it to <see cref="Vector2.Zero"/> when
        /// its length is at most <c>1e-5</c>.
        /// </summary>
        public void Normalize()
        {
            float magnitude = v.Length();
            if (magnitude <= ZeroMagnitudeEpsilon)
            {
                v = Vector2.Zero;
                return;
            }

            v /= magnitude;
        }
    }
}
