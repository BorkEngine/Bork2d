using System.Diagnostics.CodeAnalysis;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The complete, deterministic set of entries one bake produces, indexed by canonical path.
/// </summary>
/// <remarks>
/// <para>
/// Custom data contributes one MessagePack entry carrying its registered type name; each baked
/// texture contributes a <c>TextureDescriptor</c> MessagePack entry and a raw RGBA8 payload entry.
/// Entries are sorted ordinal by path, so reference validation, collision reporting, and pack
/// composition all see the same order regardless of discovery or dictionary order (D-M4-07).
/// </para>
/// <para>
/// Building the index reports a <see cref="BakeDiagnosticCodes.AssetPathCollision"/> error for a path
/// produced twice, including a MessagePack path colliding with a raw payload path (M4 §8.4). The
/// index is built before <c>PackBuilder</c> sees an entry, so a collision is a source-level diagnostic
/// rather than a writer exception.
/// </para>
/// </remarks>
internal sealed class BakedAssetIndex
{
    private readonly Dictionary<string, BakedAssetEntry> _byPath;
    private readonly BakedAssetEntry[] _entries;

    private BakedAssetIndex(
        Dictionary<string, BakedAssetEntry> byPath,
        BakedAssetEntry[] entries)
    {
        _byPath = byPath;
        _entries = entries;
    }

    /// <summary>Gets every entry in ordinal path order.</summary>
    internal IReadOnlyList<BakedAssetEntry> Entries => _entries;

    /// <summary>Looks up an entry by its canonical path.</summary>
    /// <param name="path">The canonical asset path.</param>
    /// <param name="entry">The matching entry, when found.</param>
    /// <returns><see langword="true"/> when this bake produces the path.</returns>
    internal bool TryGet(string path, [NotNullWhen(true)] out BakedAssetEntry? entry)
        => _byPath.TryGetValue(path, out entry);

    /// <summary>Builds the index and reports duplicate or cross-kind paths.</summary>
    /// <param name="assets">The bound custom-data assets, in discovery order.</param>
    /// <param name="textures">The successfully baked textures, in path order.</param>
    /// <param name="projectDirectory">The project root, used to render texture source paths.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="packPath">The pack label used in collision messages, or <see langword="null"/>.</param>
    /// <returns>The index, including the first entry for every colliding path.</returns>
    internal static BakedAssetIndex Build(
        IReadOnlyList<BoundAsset> assets,
        IReadOnlyList<BakedTexture> textures,
        string projectDirectory,
        BakeDiagnostics diagnostics,
        string? packPath = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var byPath = new Dictionary<string, BakedAssetEntry>(
            assets.Count + (textures.Count * 2),
            StringComparer.Ordinal);
        var entries = new List<BakedAssetEntry>(byPath.Capacity);

        foreach (BoundAsset asset in assets)
        {
            Add(BakedAssetEntry.ForCustomData(asset), asset.SourcePath);
        }

        foreach (BakedTexture texture in textures)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(projectDirectory, texture.SourcePath);
            Add(BakedAssetEntry.ForTextureDescriptor(texture), diagnosticPath);
            Add(BakedAssetEntry.ForTexturePayload(texture), diagnosticPath);
        }

        entries.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new BakedAssetIndex(byPath, [.. entries]);

        void Add(BakedAssetEntry entry, string diagnosticPath)
        {
            if (!byPath.TryAdd(entry.Path, entry))
            {
                BakedAssetEntry existing = byPath[entry.Path];
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.AssetPathCollision,
                    BuildCollisionMessage(existing, entry, packPath),
                    diagnosticPath,
                    assetPath: entry.Path);
                return;
            }

            entries.Add(entry);
        }
    }

    private static string BuildCollisionMessage(
        BakedAssetEntry existing,
        BakedAssetEntry incoming,
        string? packPath)
    {
        string scope = packPath is null ? string.Empty : $" in pack '{packPath}'";
        if (existing.IsRaw != incoming.IsRaw)
        {
            return $"The baked asset path '{incoming.Path}' is produced both as a MessagePack asset "
                + $"and as a raw payload{scope} by this bake.";
        }

        return $"The baked asset path '{incoming.Path}' is produced more than once{scope} "
            + $"('{existing.Kind}' and '{incoming.Kind}').";
    }
}
