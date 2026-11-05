namespace Bork2d.Bake.Svg;

/// <summary>
/// The severity of one SVG parse diagnostic.
/// </summary>
internal enum SvgDiagnosticSeverity
{
    /// <summary>An ignored unsupported feature; the parse continues.</summary>
    Info,

    /// <summary>A malformed or unsupported value; the parse fails and returns no artwork.</summary>
    Error,
}
