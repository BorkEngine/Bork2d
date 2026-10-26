namespace Bork2d.Bake.Diagnostics;

/// <summary>
/// Collects bake diagnostics and orders them deterministically.
/// </summary>
/// <remarks>
/// Sorting is by file path, line, column, code, message, then severity, all ordinal, so repeated
/// runs over the same inputs produce the same diagnostics in the same order. The bag never sorts on
/// write; call <see cref="GetSorted"/> before rendering.
/// </remarks>
internal sealed class BakeDiagnostics
{
    private readonly List<BakeDiagnostic> _items = [];

    /// <summary>Gets the number of collected diagnostics.</summary>
    internal int Count => _items.Count;

    /// <summary>Gets a value indicating whether at least one error was collected.</summary>
    internal bool HasErrors
    {
        get
        {
            foreach (BakeDiagnostic diagnostic in _items)
            {
                if (diagnostic.Severity == BakeSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Adds a diagnostic.</summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    internal void Add(BakeDiagnostic diagnostic) => _items.Add(diagnostic);

    /// <summary>Adds a diagnostic with the given fields.</summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="code">The stable <c>BKxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="filePath">The optional file path.</param>
    /// <param name="line">The optional 1-based line.</param>
    /// <param name="column">The optional 1-based column.</param>
    /// <param name="assetPath">The optional canonical asset path.</param>
    /// <param name="memberPath">The optional member path.</param>
    internal void Add(
        BakeSeverity severity,
        string code,
        string message,
        string? filePath = null,
        int? line = null,
        int? column = null,
        string? assetPath = null,
        string? memberPath = null)
        => _items.Add(new BakeDiagnostic(
            severity,
            code,
            message,
            filePath,
            line,
            column,
            assetPath,
            memberPath));

    /// <summary>Returns the collected diagnostics in deterministic order.</summary>
    /// <returns>A new array sorted by file, line, column, code, message, then severity.</returns>
    internal IReadOnlyList<BakeDiagnostic> GetSorted()
    {
        BakeDiagnostic[] sorted = [.. _items];
        Array.Sort(sorted, Compare);
        return sorted;
    }

    private static int Compare(BakeDiagnostic left, BakeDiagnostic right)
    {
        int result = string.CompareOrdinal(left.FilePath ?? string.Empty, right.FilePath ?? string.Empty);
        if (result != 0)
        {
            return result;
        }

        result = (left.Line ?? 0).CompareTo(right.Line ?? 0);
        if (result != 0)
        {
            return result;
        }

        result = (left.Column ?? 0).CompareTo(right.Column ?? 0);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.Code, right.Code);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.Message, right.Message);
        return result != 0 ? result : left.Severity.CompareTo(right.Severity);
    }
}
