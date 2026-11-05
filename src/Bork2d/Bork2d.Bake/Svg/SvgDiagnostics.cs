namespace Bork2d.Bake.Svg;

/// <summary>
/// Collects SVG parse diagnostics in document order.
/// </summary>
/// <remarks>
/// The bag is append-only and not thread-safe; one instance belongs to one parse call on the bake
/// thread. Diagnostics are not sorted, so repeated parses of the same source produce the same order.
/// </remarks>
internal sealed class SvgDiagnostics
{
    private readonly List<SvgDiagnostic> _items = [];

    /// <summary>Gets the collected diagnostics in the order the parser reported them.</summary>
    internal IReadOnlyList<SvgDiagnostic> Items => _items;

    /// <summary>Gets a value indicating whether at least one error was collected.</summary>
    internal bool HasErrors
    {
        get
        {
            foreach (SvgDiagnostic diagnostic in _items)
            {
                if (diagnostic.Severity == SvgDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Adds an error diagnostic.</summary>
    /// <param name="code">The stable <c>BK9xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="line">The optional 1-based line.</param>
    /// <param name="column">The optional 1-based column.</param>
    internal void Error(string code, string message, int? line = null, int? column = null)
        => _items.Add(new SvgDiagnostic(SvgDiagnosticSeverity.Error, code, message, line, column));

    /// <summary>Adds an informational diagnostic.</summary>
    /// <param name="code">The stable <c>BK9xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="line">The optional 1-based line.</param>
    /// <param name="column">The optional 1-based column.</param>
    internal void Info(string code, string message, int? line = null, int? column = null)
        => _items.Add(new SvgDiagnostic(SvgDiagnosticSeverity.Info, code, message, line, column));
}
