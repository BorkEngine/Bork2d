namespace Bork2d.Bake.Svg;

/// <summary>
/// A 2D affine transform in SVG matrix form: <c>x' = A*x + C*y + E</c> and
/// <c>y' = B*x + D*y + F</c>.
/// </summary>
/// <remarks>
/// Composition is explicit: <see cref="Multiply"/> applies its child transform to a point first and
/// its parent transform second. The parser accumulates a transform list as
/// <c>result = Multiply(result, next)</c>, so the rightmost function applies first, matching SVG
/// order. Angles are degrees, and rotation follows SVG's Y-down convention (a positive angle turns
/// +X toward +Y on screen).
/// </remarks>
internal readonly record struct SvgAffine(double A, double B, double C, double D, double E, double F)
{
    /// <summary>Gets the identity transform.</summary>
    internal static SvgAffine Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>Creates a translation.</summary>
    /// <param name="tx">The X translation.</param>
    /// <param name="ty">The Y translation.</param>
    /// <returns>The translation transform.</returns>
    internal static SvgAffine Translate(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    /// <summary>Creates an axis-aligned scale.</summary>
    /// <param name="sx">The X scale factor.</param>
    /// <param name="sy">The Y scale factor.</param>
    /// <returns>The scale transform.</returns>
    internal static SvgAffine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Creates a rotation about a center point.</summary>
    /// <param name="angleDegrees">The rotation angle in degrees.</param>
    /// <param name="cx">The center X.</param>
    /// <param name="cy">The center Y.</param>
    /// <returns>The rotation transform.</returns>
    /// <remarks>The center does not move, so <c>Rotate(angle, cx, cy)</c> leaves <c>(cx,cy)</c> fixed.</remarks>
    internal static SvgAffine Rotate(double angleDegrees, double cx, double cy)
    {
        double radians = angleDegrees * Math.PI / 180;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        // Translate(cx,cy) * Rotate(angle) * Translate(-cx,-cy) in one step.
        return new(cos, sin, -sin, cos, cx - (cos * cx) + (sin * cy), cy - (sin * cx) - (cos * cy));
    }

    /// <summary>Creates a horizontal skew.</summary>
    /// <param name="angleDegrees">The skew angle in degrees.</param>
    /// <returns>The skew transform.</returns>
    internal static SvgAffine SkewX(double angleDegrees)
        => new(1, 0, Math.Tan(angleDegrees * Math.PI / 180), 1, 0, 0);

    /// <summary>Creates a vertical skew.</summary>
    /// <param name="angleDegrees">The skew angle in degrees.</param>
    /// <returns>The skew transform.</returns>
    internal static SvgAffine SkewY(double angleDegrees)
        => new(1, Math.Tan(angleDegrees * Math.PI / 180), 0, 1, 0, 0);

    /// <summary>Composes two transforms so the child applies first and the parent second.</summary>
    /// <param name="parent">The transform applied last.</param>
    /// <param name="child">The transform applied first.</param>
    /// <returns>The composed transform.</returns>
    internal static SvgAffine Multiply(SvgAffine parent, SvgAffine child) => new(
        (parent.A * child.A) + (parent.C * child.B),
        (parent.B * child.A) + (parent.D * child.B),
        (parent.A * child.C) + (parent.C * child.D),
        (parent.B * child.C) + (parent.D * child.D),
        (parent.A * child.E) + (parent.C * child.F) + parent.E,
        (parent.B * child.E) + (parent.D * child.F) + parent.F);

    /// <summary>Transforms one point.</summary>
    /// <param name="point">The point to transform.</param>
    /// <returns>The transformed point.</returns>
    internal SvgPoint Transform(SvgPoint point)
        => new((A * point.X) + (C * point.Y) + E, (B * point.X) + (D * point.Y) + F);

    /// <summary>Gets the transform determinant; zero means the transform is singular.</summary>
    internal double Determinant => (A * D) - (B * C);
}
