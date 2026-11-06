namespace Bork2d.Bake.Fonts;

/// <summary>One font bake diagnostic.</summary>
/// <param name="Severity">Whether the diagnostic fails the bake.</param>
/// <param name="Code">The stable <c>BK93xx</c> code.</param>
/// <param name="Message">The human-readable message.</param>
internal readonly record struct FontDiagnostic(
    FontDiagnosticSeverity Severity,
    string Code,
    string Message);
