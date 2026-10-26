using System.Diagnostics.CodeAnalysis;
using Bork2d.Api;

namespace Bork2d.Assets;

/// <summary>
/// The merged asset filesystem: an immutable overlay stack of a base pack plus later-wins
/// overlays.
/// </summary>
/// <remarks>
/// <para>
/// The stack starts empty. The first <see cref="LoadPack"/> mount is the base pack; every later
/// mount is placed above it, so a later pack shadows an earlier pack by canonical path. Mounting
/// the same file more than once is allowed: the later mount wins, and there is no unload,
/// replacement, loose-file fallback, or retry in v1.
/// </para>
/// <para>
/// Mounting parses and validates the pack outside the swap lock and publishes a new immutable
/// snapshot atomically. Readers never take the mount lock: they capture the current snapshot with
/// <see cref="Volatile.Read{T}(ref T)"/>, so a lookup never observes a partially built stack. A
/// failed mount leaves the previous snapshot and its archives untouched.
/// </para>
/// <para>
/// <see cref="Dispose"/> closes every mounted archive and is idempotent. After disposal every
/// member throws <see cref="ObjectDisposedException"/>, and previously resolved entries fail when
/// their payload is read. Disposal must not race an active payload read; the asset service stops
/// its loader before disposing the filesystem.
/// </para>
/// </remarks>
internal sealed class AssetFileSystem : IDisposable
{
    private readonly Lock _mountLock = new();
    private Snapshot? _snapshot = Snapshot.Empty;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetFileSystem"/> class with an empty stack.
    /// </summary>
    internal AssetFileSystem()
    {
    }

    /// <summary>
    /// Tests whether the merged filesystem contains a canonical asset path.
    /// </summary>
    /// <param name="assetPath">A caller-supplied asset path; it is normalized and validated.</param>
    /// <returns><see langword="true"/> when a visible entry has the exact path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetPath"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="assetPath"/> is not a valid asset path.</exception>
    /// <exception cref="ObjectDisposedException">The filesystem has been disposed.</exception>
    internal bool Exists(string assetPath)
    {
        Snapshot snapshot = GetSnapshot();
        string canonicalPath = AssetPath.Normalize(assetPath);
        return snapshot.Entries.ContainsKey(canonicalPath);
    }

    /// <summary>
    /// Returns a materialized snapshot of the visible assets, one per canonical path.
    /// </summary>
    /// <param name="prefix">
    /// A canonical path prefix, or <see langword="null"/> for the whole tree. A non-null prefix
    /// matches the path equal to the prefix or paths below it on a <c>/</c> segment boundary.
    /// </param>
    /// <returns>The matching entries sorted by <see cref="AssetInfo.Path"/> ordinal.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> is an invalid asset path. An empty string is invalid; use
    /// <see langword="null"/> for all assets.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The filesystem has been disposed.</exception>
    internal IReadOnlyList<AssetInfo> Browse(string? prefix = null)
    {
        Snapshot snapshot = GetSnapshot();
        string? canonicalPrefix = prefix is null ? null : AssetPath.Normalize(prefix);
        string prefixWithSeparator = canonicalPrefix is null ? string.Empty : canonicalPrefix + "/";

        if (snapshot.Entries.Count == 0)
        {
            return Array.Empty<AssetInfo>();
        }

        var results = new List<AssetInfo>(snapshot.Entries.Count);
        foreach (ResolvedAsset entry in snapshot.Entries.Values)
        {
            if (canonicalPrefix is null
                || string.Equals(entry.Path, canonicalPrefix, StringComparison.Ordinal)
                || entry.Path.StartsWith(prefixWithSeparator, StringComparison.Ordinal))
            {
                results.Add(
                    new AssetInfo(
                        entry.Path,
                        entry.IsRaw,
                        entry.IsCompressed,
                        entry.Length,
                        entry.Size,
                        entry.PackPath));
            }
        }

        results.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
        return results;
    }

    /// <summary>
    /// Looks up the winning entry for an already canonical asset path.
    /// </summary>
    /// <param name="canonicalPath">
    /// The canonical asset path. Callers validate public input with <see cref="AssetPath.Normalize"/>
    /// first; this lookup is exact and does not normalize.
    /// </param>
    /// <param name="entry">The winning entry, or <see langword="null"/> when the path is absent.</param>
    /// <returns><see langword="true"/> when the merged filesystem contains the path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="canonicalPath"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The filesystem has been disposed.</exception>
    internal bool TryGetEntry(
        string canonicalPath,
        [NotNullWhen(true)] out ResolvedAsset? entry)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        Snapshot snapshot = GetSnapshot();
        return snapshot.Entries.TryGetValue(canonicalPath, out entry);
    }

    /// <summary>
    /// Gets the number of mounted packs in the current stack.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The filesystem has been disposed.</exception>
    internal int PackCount => GetSnapshot().Archives.Length;

    /// <summary>
    /// Synchronously opens, validates, and mounts a pack above the current stack.
    /// </summary>
    /// <param name="packPath">
    /// The filesystem path of the pack. Relative paths resolve against the process current
    /// directory at the time of the call; absolute paths are accepted. The value is kept as the
    /// supplied pack identifier and is never normalized as an asset path.
    /// </param>
    /// <returns>Mount details for the start/overlay log events.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="packPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="packPath"/> is empty or whitespace, or is not a valid filesystem path.
    /// </exception>
    /// <exception cref="Bork2dException">
    /// The file cannot be opened or is not a valid v1 pack. The stack is unchanged.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The filesystem has been disposed.</exception>
    internal PackMountInfo LoadPack(string packPath)
    {
        _ = GetSnapshot();
        ArgumentNullException.ThrowIfNull(packPath);
        if (string.IsNullOrWhiteSpace(packPath))
        {
            throw new ArgumentException(
                "The pack path must not be empty or whitespace.",
                nameof(packPath));
        }

        PackArchive archive = PackArchive.Open(packPath);
        try
        {
            lock (_mountLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(AssetFileSystem));
                }

                Snapshot current = Volatile.Read(ref _snapshot)
                    ?? throw new ObjectDisposedException(nameof(AssetFileSystem));
                Snapshot next = CreateSnapshot(current, archive, packPath);
                Volatile.Write(ref _snapshot, next);
            }

            long storedBytes = 0;
            foreach (PackEntry entry in archive.Entries)
            {
                storedBytes += entry.Length;
            }

            return new PackMountInfo(
                packPath,
                archive.DisplayPath,
                PackFormat.CurrentVersion,
                archive.Entries.Count,
                storedBytes);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Snapshot? snapshot;
        lock (_mountLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            snapshot = Volatile.Read(ref _snapshot);
            Volatile.Write(ref _snapshot, null);
        }

        if (snapshot is null)
        {
            return;
        }

        foreach (PackArchive archive in snapshot.Archives)
        {
            archive.Dispose();
        }
    }

    private static Snapshot CreateSnapshot(
        Snapshot current,
        PackArchive archive,
        string packPath)
    {
        var archives = new PackArchive[current.Archives.Length + 1];
        Array.Copy(current.Archives, archives, current.Archives.Length);
        archives[^1] = archive;

        var entries = new Dictionary<string, ResolvedAsset>(
            current.Entries,
            StringComparer.Ordinal);
        foreach (PackEntry packEntry in archive.Entries)
        {
            entries[packEntry.Path] = new ResolvedAsset(
                packEntry.Path,
                packEntry.IsRaw,
                packEntry.IsCompressed,
                packEntry.Length,
                packEntry.Size,
                packPath,
                archive,
                packEntry);
        }

        return new Snapshot(archives, entries);
    }

    private Snapshot GetSnapshot()
    {
        Snapshot? snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is null)
        {
            throw new ObjectDisposedException(nameof(AssetFileSystem));
        }

        return snapshot;
    }

    private sealed class Snapshot
    {
        internal static readonly Snapshot Empty = new(
            [],
            new Dictionary<string, ResolvedAsset>(StringComparer.Ordinal));

        internal Snapshot(
            PackArchive[] archives,
            Dictionary<string, ResolvedAsset> entries)
        {
            Archives = archives;
            Entries = entries;
        }

        internal PackArchive[] Archives { get; }

        internal Dictionary<string, ResolvedAsset> Entries { get; }
    }
}
