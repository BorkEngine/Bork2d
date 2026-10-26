namespace Bork2d.Bake.Hosting;

/// <summary>
/// A deterministic, de-duplicated set of generated-host assembly references keyed by simple name.
/// </summary>
/// <remarks>
/// The first path added for a name wins, and enumeration is ordinal by assembly name, so two
/// generations from the same inputs produce identical project text. Duplicates matter because the
/// <c>Project.Common</c> output directory, the tooling directory, and the transitive closure can all
/// contain the same assembly.
/// </remarks>
internal sealed class BakeHostReferenceSet
{
    private readonly SortedDictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the references in ordinal assembly-name order.</summary>
    internal IReadOnlyList<BakeHostReference> References
        => [.. _paths.Select(pair => new BakeHostReference(pair.Key, pair.Value))];

    /// <summary>Adds a reference when its assembly name is not already present.</summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <param name="path">The absolute assembly path.</param>
    /// <returns><see langword="true"/> when the reference was added.</returns>
    internal bool TryAdd(string assemblyName, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_paths.ContainsKey(assemblyName) || !_filePaths.Add(path))
        {
            return false;
        }

        _paths.Add(assemblyName, path);
        return true;
    }

    /// <summary>Tests whether a reference with the given assembly name is present.</summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns><see langword="true"/> when the name is present.</returns>
    internal bool Contains(string assemblyName) => _paths.ContainsKey(assemblyName);
}
