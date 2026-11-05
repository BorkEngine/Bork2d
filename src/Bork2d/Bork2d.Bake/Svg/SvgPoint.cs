namespace Bork2d.Bake.Svg;

/// <summary>
/// A point in SVG user coordinates (Y down) or in artboard-local coordinates (Y up).
/// </summary>
/// <remarks>
/// The parser works in SVG user coordinates until the artboard mapping, then produces artboard-local
/// Y-up points. A point carries no coordinate-space tag; the owning <see cref="SvgContour"/>
/// determines the space. Values are always finite.
/// </remarks>
internal readonly record struct SvgPoint(double X, double Y);
