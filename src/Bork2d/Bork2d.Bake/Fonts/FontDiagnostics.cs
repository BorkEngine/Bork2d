namespace Bork2d.Bake.Fonts;

/// <summary>
/// Collects font bake diagnostics in the order the bake reported them.
/// </summary>
/// <remarks>
/// The bag is append-only and not thread-safe; one instance belongs to one bake on the bake thread.
/// Diagnostics are not sorted, so repeated bakes of the same source produce the same order.
/// </remarks>
internal sealed class FontDiagnostics
{
    private readonly List<FontDiagnostic> _items = [];

    /// <summary>Gets the collected diagnostics in report order.</summary>
    internal IReadOnlyList<FontDiagnostic> Items => _items;

    /// <summary>Gets a value indicating whether at least one error was collected.</summary>
    internal bool HasErrors
    {
        get
        {
            foreach (FontDiagnostic diagnostic in _items)
            {
                if (diagnostic.Severity == FontDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Adds an error diagnostic.</summary>
    /// <param name="code">The stable <c>BK93xx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    internal void Error(string code, string message)
        => _items.Add(new FontDiagnostic(FontDiagnosticSeverity.Error, code, message));

    /// <summary>Adds an informational diagnostic.</summary>
    /// <param name="code">The stable <c>BK93xx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    internal void Info(string code, string message)
        => _items.Add(new FontDiagnostic(FontDiagnosticSeverity.Info, code, message));
}
