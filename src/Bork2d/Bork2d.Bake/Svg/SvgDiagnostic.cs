namespace Bork2d.Bake.Svg;

/// <summary>
/// One SVG parse diagnostic with an optional source location.
/// </summary>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Code">The <c>BK9xxx</c> SVG code.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="Line">The 1-based XML line, or <see langword="null"/> when unknown.</param>
/// <param name="Column">The 1-based XML column, or <see langword="null"/> when unknown.</param>
/// <remarks>
/// The parser reports element-level problems at the element's start tag. Path-data problems add the
/// zero-based character offset of the failing token to <paramref name="Message"/> because the
/// diagnostic has no character column of its own. The caller's diagnostic sink supplies the source
/// path and formats the location.
/// </remarks>
internal readonly record struct SvgDiagnostic(
    SvgDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? Line,
    int? Column);
