using Tomlyn.Syntax;

namespace Bork2d.Bake.Toml;

/// <summary>
/// Maps segment-aware TOML key paths to their syntax-tree spans and records duplicate keys.
/// </summary>
/// <remarks>
/// <para>
/// The index is built from <see cref="SyntaxParser"/>'s <see cref="DocumentSyntax"/>: root keys,
/// table headers, table-array elements, dotted keys, inline tables, and arrays of inline tables.
/// Tomlyn's positions are 0-based; consumers add one when creating a <c>BakeDiagnostic</c>.
/// </para>
/// <para>
/// It intentionally does not convert the document to a model; the descriptor parser reads values
/// from the Tomlyn model and resolves their source locations through this index.
/// </para>
/// </remarks>
internal sealed class TomlSyntaxIndex
{
    private readonly Dictionary<string, SourceSpan> _keySpans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceSpan> _valueSpans = new(StringComparer.Ordinal);
    private readonly HashSet<string> _keyPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _explicitTables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _elementCounts = new(StringComparer.Ordinal);
    private readonly List<(string DisplayPath, SourceSpan Span)> _duplicates = [];

    private TomlSyntaxIndex()
    {
    }

    /// <summary>Gets the duplicate keys found while indexing, in source order.</summary>
    internal IReadOnlyList<(string DisplayPath, SourceSpan Span)> Duplicates => _duplicates;

    /// <summary>Builds the span index for a parsed document.</summary>
    /// <param name="document">The parsed TOML document.</param>
    /// <returns>The span index.</returns>
    internal static TomlSyntaxIndex Create(DocumentSyntax document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = new TomlSyntaxIndex();

        foreach (KeyValueSyntax keyValue in document.KeyValues)
        {
            index.AddKeyValue(keyValue, TomlPath.Root, parentDisplay: string.Empty);
        }

        foreach (TableSyntaxBase table in document.Tables)
        {
            if (table.Name is not KeySyntax name)
            {
                continue;
            }

            List<string> segments = GetSegments(name);
            if (segments.Count == 0)
            {
                continue;
            }

            TomlPath tablePath = TomlPath.Root;
            foreach (string segment in segments)
            {
                tablePath = tablePath.Child(segment);
            }

            string tableDisplay = string.Join('.', segments);

            if (table is TableArraySyntax array)
            {
                int elementIndex = index.NextElementIndex(tablePath);
                TomlPath elementPath = tablePath.Element(elementIndex);
                string elementDisplay = $"{tableDisplay}[{elementIndex}]";

                index.AddTable(elementPath, elementDisplay, name.Span);
                foreach (KeyValueSyntax keyValue in array.Items)
                {
                    index.AddKeyValue(keyValue, elementPath, elementDisplay);
                }

                continue;
            }

            index.AddTable(tablePath, tableDisplay, name.Span);
            foreach (KeyValueSyntax keyValue in table.Items)
            {
                index.AddKeyValue(keyValue, tablePath, tableDisplay);
            }
        }

        return index;
    }

    /// <summary>Looks up the key-token span for a path.</summary>
    /// <param name="path">The key path.</param>
    /// <param name="span">The span when found.</param>
    /// <returns><see langword="true"/> when the path has a key span.</returns>
    internal bool TryGetKeySpan(TomlPath path, out SourceSpan span)
        => _keySpans.TryGetValue(path.Key, out span);

    /// <summary>Looks up the value-token span for a path.</summary>
    /// <param name="path">The key path.</param>
    /// <param name="span">The span when found.</param>
    /// <returns><see langword="true"/> when the path has a value span.</returns>
    internal bool TryGetValueSpan(TomlPath path, out SourceSpan span)
        => _valueSpans.TryGetValue(path.Key, out span);

    private int NextElementIndex(TomlPath arrayPath)
    {
        int index = _elementCounts.TryGetValue(arrayPath.Key, out int existing) ? existing : 0;
        _elementCounts[arrayPath.Key] = index + 1;
        return index;
    }

    private void AddTable(TomlPath path, string displayPath, SourceSpan span)
    {
        if (_explicitTables.Contains(path.Key) || _keyPaths.Contains(path.Key))
        {
            _duplicates.Add((displayPath, span));
            return;
        }

        _explicitTables.Add(path.Key);
        _keySpans[path.Key] = span;
    }

    private void AddKeyValue(KeyValueSyntax? keyValue, TomlPath parentPath, string parentDisplay)
    {
        if (keyValue?.Key is not KeySyntax key || keyValue.Value is not ValueSyntax value)
        {
            return;
        }

        List<string> segments = GetSegments(key);
        if (segments.Count == 0)
        {
            return;
        }

        TomlPath path = parentPath;
        string displayPath = parentDisplay;

        for (int i = 0; i < segments.Count; i++)
        {
            path = path.Child(segments[i]);
            displayPath = displayPath.Length == 0
                ? segments[i]
                : displayPath + "." + segments[i];

            if (i < segments.Count - 1)
            {
                _keySpans.TryAdd(path.Key, key.Span);
                continue;
            }

            if (!_keyPaths.Add(path.Key) || _explicitTables.Contains(path.Key))
            {
                _duplicates.Add((displayPath, key.Span));
            }

            _keySpans[path.Key] = key.Span;
            _valueSpans[path.Key] = value.Span;
            IndexValue(value, path, displayPath);
        }
    }

    private void IndexValue(ValueSyntax value, TomlPath path, string displayPath)
    {
        switch (value)
        {
            case InlineTableSyntax inline:
                foreach (InlineTableItemSyntax? item in inline.Items)
                {
                    if (item is not null)
                    {
                        AddKeyValue(item.KeyValue, path, displayPath);
                    }
                }

                break;

            case ArraySyntax array:
                for (int i = 0; i < array.Items.ChildrenCount; i++)
                {
                    if (array.Items.GetChild(i) is ArrayItemSyntax item
                        && item.Value is ValueSyntax itemValue)
                    {
                        IndexValue(itemValue, path.Element(i), $"{displayPath}[{i}]");
                    }
                }

                break;
        }
    }

    private static List<string> GetSegments(KeySyntax key)
    {
        var segments = new List<string>();
        string first = GetKeyText(key.Key);
        if (first.Length > 0)
        {
            segments.Add(first);
        }

        foreach (DottedKeyItemSyntax item in key.DotKeys)
        {
            string segment = GetKeyText(item.Key);
            if (segment.Length > 0)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    private static string GetKeyText(BareKeyOrStringValueSyntax? key)
        => key switch
        {
            StringValueSyntax stringValue => stringValue.Value ?? string.Empty,
            BareKeySyntax bareKey => bareKey.Key?.Text ?? string.Empty,
            _ => key?.ToString() ?? string.Empty,
        };
}
