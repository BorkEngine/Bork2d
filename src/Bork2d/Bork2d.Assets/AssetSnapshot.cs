using System.Diagnostics.CodeAnalysis;

namespace Bork2d.Assets;

/// <summary>
/// An immutable snapshot of the merged filesystem's visible entries and mounted archives.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is captured once per request at enqueue time. Every lookup for that request — the
/// winning asset entry, a sprite's texture descriptor, and the descriptor's payload — uses the same
/// snapshot, so a later <see cref="AssetFileSystem.LoadPack"/> mount can never change an in-flight
/// request. New lookups capture the new snapshot.
/// </para>
/// <para>
/// The contained dictionaries and archive array are never mutated after construction. Snapshots
/// share the mounted archives, which stay open for the process lifetime.
/// </para>
/// </remarks>
internal sealed class AssetSnapshot
{
    /// <summary>The snapshot of an empty filesystem: no archives and no entries.</summary>
    internal static readonly AssetSnapshot Empty = new(
        [],
        new Dictionary<string, ResolvedAsset>(StringComparer.Ordinal));

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetSnapshot"/> class.
    /// </summary>
    /// <param name="archives">The mounted archives, bottom first.</param>
    /// <param name="entries">The winning entry for every visible canonical path.</param>
    internal AssetSnapshot(
        PackArchive[] archives,
        Dictionary<string, ResolvedAsset> entries)
    {
        Archives = archives;
        Entries = entries;
    }

    /// <summary>Gets the mounted archives, bottom first.</summary>
    internal PackArchive[] Archives { get; }

    /// <summary>Gets the winning entry for every visible canonical path.</summary>
    internal Dictionary<string, ResolvedAsset> Entries { get; }

    /// <summary>
    /// Looks up the winning entry for an already canonical asset path.
    /// </summary>
    /// <param name="canonicalPath">The canonical asset path; the lookup is exact.</param>
    /// <param name="entry">The winning entry, or <see langword="null"/> when the path is absent.</param>
    /// <returns><see langword="true"/> when the snapshot contains the path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canonicalPath"/> is null.</exception>
    internal bool TryGetEntry(
        string canonicalPath,
        [NotNullWhen(true)] out ResolvedAsset? entry)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        return Entries.TryGetValue(canonicalPath, out entry);
    }
}
