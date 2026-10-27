using Bork2d.Engine;
using Bork2d.Engine.Assets.Vfs;
using PolyType;

namespace Bork2d.Engine.Assets.Pack;

/// <summary>
/// Builds a v1 asset pack for tooling and tests.
/// </summary>
/// <remarks>
/// <para>
/// This is the only public tooling surface in <c>Bork2d.Engine.Assets</c>; game code references only
/// <c>Bork2d.Engine</c> and <c>Bork2d.Runtime</c>. The builder stores payloads in memory, is not
/// thread-safe, and never consumes itself: <see cref="Build"/> and <see cref="Write(string)"/> may
/// be called repeatedly, and entries may be added after a build.
/// </para>
/// <para>
/// Entry paths use the canonical asset-path rules. Entries are sorted by canonical path when
/// written. There is no byte-for-byte determinism guarantee: Zstd output may vary across runs and
/// runtime versions.
/// </para>
/// <para>
/// <see cref="Build"/> and <see cref="Write(string)"/> validate every non-null
/// <c>AssetRef&lt;T&gt;</c> and <c>DataRef</c> in every MessagePack asset: the referenced path must
/// be canonical and present, and the reference kind must match the target entry (asset references
/// target MessagePack entries; data references target raw entries). A null path is an absent
/// reference and is valid. Field-level asset validation belongs to the bake tooling, not the pack
/// builder.
/// </para>
/// </remarks>
public sealed class PackBuilder
{
    private readonly PackCompressionMode _mode;
    private readonly Dictionary<string, PackSourceEntry> _entries = new(StringComparer.Ordinal);
    private Dictionary<string, bool>? _externalAssets;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackBuilder"/> class.
    /// </summary>
    /// <param name="mode">The payload compression policy.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a known mode.</exception>
    public PackBuilder(PackCompressionMode mode)
    {
        if (mode is not PackCompressionMode.Debug and not PackCompressionMode.Release)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown pack compression mode.");
        }

        _mode = mode;
    }

    /// <summary>
    /// Adds an opaque payload.
    /// </summary>
    /// <param name="path">The asset path; it is normalized to canonical form and copied.</param>
    /// <param name="payload">The payload bytes, which are copied into the builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="Bork2dException">The canonical path is already present.</exception>
    public void AddRaw(string path, ReadOnlySpan<byte> payload)
    {
        string canonicalPath = AssetPath.Normalize(path);
        ThrowIfDuplicate(canonicalPath);
        _entries.Add(
            canonicalPath,
            new PackSourceEntry(canonicalPath, isRaw: true, payload.ToArray(), references: []));
    }

    /// <summary>
    /// Serializes a value with its source-generated MessagePack shape and adds it as an asset.
    /// </summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="path">The asset path; it is normalized to canonical form.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    /// <exception cref="Bork2dException">
    /// The canonical path is already present, or serialization failed.
    /// </exception>
    public void AddMessagePack<T>(string path, T value)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(value);

        string canonicalPath = AssetPath.Normalize(path);
        ThrowIfDuplicate(canonicalPath);

        byte[] payload;
        IReadOnlyList<AssetReferenceInfo> references;
        try
        {
            references = AssetReferenceCollector.Collect(canonicalPath, value);
            payload = PackSerialization.SerializeAsset(value);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new Bork2dException(
                $"Failed to serialize MessagePack asset '{canonicalPath}': {exception.Message}",
                exception);
        }

        _entries.Add(
            canonicalPath,
            new PackSourceEntry(canonicalPath, isRaw: false, payload, references));
    }

    /// <summary>
    /// Builds the complete pack in one array.
    /// </summary>
    /// <returns>The pack bytes.</returns>
    /// <exception cref="Bork2dException">
    /// An asset reference is invalid, or the pack exceeds <see cref="Array.MaxLength"/>.
    /// </exception>
    public byte[] Build()
    {
        PackLayout layout = CreateLayout();
        return layout.ToArray();
    }

    /// <summary>
    /// Writes the complete pack to an overwritten file.
    /// </summary>
    /// <param name="path">
    /// The filesystem path; relative paths resolve against the process current directory.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="Bork2dException">
    /// An asset reference is invalid, or the pack could not be written.
    /// </exception>
    public void Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The pack path must not be empty or whitespace.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid pack path '{path}': {exception.Message}",
                nameof(path),
                exception);
        }

        PackLayout layout = CreateLayout();
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            layout.WriteTo(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            throw new Bork2dException(
                $"Failed to write asset pack '{fullPath}': {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Streams the complete pack to a caller-supplied stream and leaves the stream open.
    /// </summary>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="Bork2dException">
    /// An asset reference is invalid, or the pack could not be written.
    /// </exception>
    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The pack destination must be writable.", nameof(destination));
        }

        PackLayout layout = CreateLayout();
        try
        {
            layout.WriteTo(destination);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            throw new Bork2dException(
                $"Failed to write the asset pack: {exception.Message}",
                exception);
        }
    }

    private PackLayout CreateLayout()
    {
        ValidateReferences();

        var source = new List<PackSourceEntry>(_entries.Count);
        source.AddRange(_entries.Values);

        try
        {
            return PackLayout.Create(source, _mode);
        }
        catch (OverflowException exception)
        {
            throw new Bork2dException(
                "The pack size exceeds the v1 field widths or the supported file length.",
                exception);
        }
    }

    /// <summary>
    /// Declares assets that live in another pack (the main/base pack) for reference validation.
    /// </summary>
    /// <remarks>
    /// This is the internal multi-pack tooling seam (D-M4-14). A DLC pack may reference an asset in
    /// the main pack; the baker supplies the main pack's entries here and the runtime resolves the
    /// reference against the mounted pack stack. The main pack never calls this, so its references
    /// remain strictly local. The on-disk format and the public surface do not change.
    /// </remarks>
    /// <param name="assets">The external assets available to this pack's references.</param>
    internal void SetExternalAssets(IReadOnlyList<PackExternalAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var external = new Dictionary<string, bool>(assets.Count, StringComparer.Ordinal);
        foreach (PackExternalAsset asset in assets)
        {
            string canonicalPath = AssetPath.Normalize(asset.Path);
            if (!external.TryAdd(canonicalPath, asset.IsRaw))
            {
                throw new Bork2dException(
                    $"Duplicate external asset path '{canonicalPath}'.");
            }
        }

        _externalAssets = external;
    }

    private void ValidateReferences()
    {
        foreach (PackSourceEntry entry in _entries.Values)
        {
            foreach (AssetReferenceInfo reference in entry.References)
            {
                if (reference.Path is null)
                {
                    continue;
                }

                string location = reference.PropertyPath.Length == 0
                    ? $"Asset '{reference.SourcePath}'"
                    : $"Asset '{reference.SourcePath}' at '{reference.PropertyPath}'";

                if (reference.Path.Length == 0)
                {
                    throw new Bork2dException(
                        $"{location} has an empty reference path.");
                }

                if (!AssetPath.TryValidateCanonical(reference.Path, out string? reason))
                {
                    throw new Bork2dException(
                        $"{location} references the non-canonical path "
                            + $"'{reference.Path}': {reason}");
                }

                if (_entries.TryGetValue(reference.Path, out PackSourceEntry? target))
                {
                    if (reference.IsData == target.IsRaw)
                    {
                        continue;
                    }

                    string expected = reference.IsData
                        ? "raw data"
                        : "a MessagePack asset";
                    string actual = target.IsRaw ? "raw data" : "a MessagePack asset";
                    throw new Bork2dException(
                        $"{location} references '{reference.Path}' as {expected}, but the entry is "
                            + $"{actual}.");
                }

                if (_externalAssets is not null
                    && _externalAssets.TryGetValue(reference.Path, out bool externalIsRaw))
                {
                    if (reference.IsData == externalIsRaw)
                    {
                        continue;
                    }

                    string expected = reference.IsData
                        ? "raw data"
                        : "a MessagePack asset";
                    string actual = externalIsRaw ? "raw data" : "a MessagePack asset";
                    throw new Bork2dException(
                        $"{location} references the external asset '{reference.Path}' as "
                            + $"{expected}, but the entry is {actual}.");
                }

                throw new Bork2dException(
                    $"{location} references '{reference.Path}', which is not present in the "
                        + "pack.");
            }
        }
    }

    private void ThrowIfDuplicate(string canonicalPath)
    {
        if (_entries.ContainsKey(canonicalPath))
        {
            throw new Bork2dException($"Duplicate asset path '{canonicalPath}'.");
        }
    }
}
