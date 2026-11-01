using System.Numerics;

namespace Bork2d.Engine.Mathematics;

/// <summary>
/// The static Unity-style approximate equality helper for <see cref="Vector2"/>.
/// </summary>
/// <remarks>
/// C# extension blocks lower every instance extension member to a static method that takes the
/// receiver as its first parameter, so a static <c>ApproximatelyEquals(Vector2, Vector2)</c> cannot
/// share a declaring class with the instance <c>ApproximatelyEquals(Vector2)</c> in
/// <see cref="Vector2Extensions"/>. The two members therefore live in separate classes in the same
/// namespace, which keeps both call forms available: <c>Vector2.ApproximatelyEquals(a, b)</c> and
/// <c>a.ApproximatelyEquals(b)</c>.
/// </remarks>
public static class Vector2ApproximatelyExtensions
{
    private const float ApproximatelyEqualEpsilon = 1e-10f;

    extension(Vector2 v)
    {
        /// <summary>
        /// Determines whether two vectors are equal within Unity's approximate comparison.
        /// </summary>
        /// <param name="a">The first vector.</param>
        /// <param name="b">The second vector.</param>
        /// <returns>
        /// <see langword="true"/> when the squared difference is below <c>1e-10</c>; otherwise
        /// <see langword="false"/>.
        /// </returns>
        public static bool ApproximatelyEquals(Vector2 a, Vector2 b)
            => (a - b).LengthSquared() < ApproximatelyEqualEpsilon;
    }
}
