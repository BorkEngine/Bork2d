namespace Bork2d.Bake.Diagnostics;

/// <summary>
/// One bake diagnostic with an optional source location.
/// </summary>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Code">The stable <c>BKxxxx</c> code.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="FilePath">
/// The descriptor or source path, project-relative when under the asset root and absolute
/// otherwise; <see langword="null"/> when the diagnostic has no file.
/// </param>
/// <param name="Line">The 1-based line, or <see langword="null"/> when unknown.</param>
/// <param name="Column">The 1-based column, or <see langword="null"/> when unknown.</param>
/// <param name="AssetPath">The canonical asset path, when the diagnostic is about a baked asset.</param>
/// <param name="MemberPath">The member path, for example <c>items[2].icon</c>.</param>
/// <remarks>
/// A source span is always 1-based in this model. Tomlyn's syntax positions are 0-based and are
/// converted by the parser; a diagnostic without a syntax span carries only <paramref name="FilePath"/>.
/// </remarks>
internal readonly record struct BakeDiagnostic(
    BakeSeverity Severity,
    string Code,
    string Message,
    string? FilePath,
    int? Line,
    int? Column,
    string? AssetPath,
    string? MemberPath)
{
    /// <summary>
    /// Formats the diagnostic in the MSBuild-canonical form
    /// <c>path(line,col): error BKxxxx: message</c>.
    /// </summary>
    /// <returns>The canonical single-line rendering.</returns>
    internal string Format()
    {
        string severity = Severity switch
        {
            BakeSeverity.Warning => "warning",
            BakeSeverity.Info => "info",
            _ => "error",
        };

        string location = FilePath switch
        {
            null => string.Empty,
            _ when Line is int line && Column is int column => $"{FilePath}({line},{column}): ",
            _ when Line is int line => $"{FilePath}({line}): ",
            _ => $"{FilePath}: ",
        };

        return $"{location}{severity} {Code}: {Message}";
    }
}
