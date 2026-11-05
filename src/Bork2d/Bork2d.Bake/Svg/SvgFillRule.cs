namespace Bork2d.Bake.Svg;

/// <summary>
/// The SVG fill rule of one shape.
/// </summary>
internal enum SvgFillRule
{
    /// <summary>The nonzero winding rule.</summary>
    NonZero = 0,

    /// <summary>The even-odd rule; the parser converts it to the nonzero representation.</summary>
    EvenOdd = 1,
}
