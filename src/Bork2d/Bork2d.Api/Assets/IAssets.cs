using PolyType;

namespace Bork2d.Api.Assets;

/// <summary>
/// Provides read-only access to the asset packs mounted for the app.
/// </summary>
/// <remarks>
/// <para>
/// Asset paths are canonical runtime keys: invariant lowercase, <c>/</c> separators, no leading or
/// trailing slash, no empty or <c>.</c>/<c>..</c> segments, and segments contain only lowercase
/// letters, digits, <c>_</c>, and <c>-</c> (so source extensions are rejected). Public input is
/// normalized first (<c>\</c> becomes <c>/</c> and the path is lowercased), then validated: null
/// throws <see cref="ArgumentNullException"/> and any other invalid path throws
/// <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// All members are thread-safe. Packs are mounted synchronously; later packs shadow earlier packs
/// by canonical path and there is no unload in v1. Asset loads complete asynchronously. The engine
/// exposes the service through <c>IEngine.Assets</c>.
/// </para>
/// <para>
/// A mounted pack is trusted content. The loader resolves entries and deserializes them, but it
/// does not validate asset fields or follow <see cref="AssetRef{TAsset}"/>/
/// <see cref="DataRef"/> links. Pack creation validates reference integrity; the bake tooling
/// validates source fields. A missing linked asset faults when that link is loaded.
/// </para>
/// </remarks>
public interface IAssets
{
    /// <summary>
    /// Loads a MessagePack asset using the compiled generated shape on
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The asset type. It must satisfy <see cref="IShapeable{T}"/> through a compiled
    /// <c>[GenerateShape]</c> annotation; reflection-shape fallback is not supported.
    /// </typeparam>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <returns>
    /// A task that produces a fresh deserialized instance per call.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="assetPath"/> is not a valid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// The returned task faults when the path is not in the merged filesystem, when the winning
    /// entry is raw rather than MessagePack, or when decoding fails. The deserialized value is
    /// returned as stored; asset fields and references are not validated here. Use
    /// <see cref="LoadData(string)"/> for opaque bytes.
    /// </remarks>
    Task<T> LoadAsset<T>(string assetPath)
        where T : class, IShapeable<T>;

    /// <summary>
    /// Loads the MessagePack asset named by a typed reference.
    /// </summary>
    /// <typeparam name="T">The referenced asset type.</typeparam>
    /// <param name="reference">The typed asset reference.</param>
    /// <returns>A task that produces a fresh deserialized instance per call.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="reference"/> has no path, or its path is not a valid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// This is equivalent to <see cref="LoadAsset{T}(string)"/> with the reference's <c>Path</c>
    /// when the reference has a path. An absent reference is not a valid load request.
    /// </remarks>
    Task<T> LoadAsset<T>(AssetRef<T> reference)
        where T : class, IShapeable<T>;

    /// <summary>
    /// Loads the decompressed bytes of an asset, whether it is raw or MessagePack.
    /// </summary>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <returns>A task that produces a fresh byte array per call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="assetPath"/> is not a valid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// The returned task faults when the path is not in the merged filesystem or when the declared
    /// size exceeds <see cref="Array.MaxLength"/>. The bytes are decompressed for compressed
    /// entries.
    /// </remarks>
    Task<byte[]> LoadData(string assetPath);

    /// <summary>
    /// Loads the decompressed bytes named by a raw-data reference.
    /// </summary>
    /// <param name="reference">The raw-data reference.</param>
    /// <returns>A task that produces a fresh byte array per call.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="reference"/> has no path, or its path is not a valid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// This is equivalent to <see cref="LoadData(string)"/> with <see cref="DataRef.Path"/> when
    /// the reference has a path. An absent reference is not a valid load request.
    /// </remarks>
    Task<byte[]> LoadData(DataRef reference);

    /// <summary>
    /// Synchronously opens, validates, and mounts a pack above the current pack stack.
    /// </summary>
    /// <param name="packPath">
    /// The filesystem path of the pack. Relative paths resolve against the process current
    /// directory at the time of the call; absolute paths are accepted.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="packPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="packPath"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="Bork2dException">
    /// The pack cannot be opened or is not a valid v1 pack. The current stack is unchanged.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// Later mounts shadow earlier ones by canonical path. Mounting the same file twice mounts it
    /// twice; the later mount wins. There is no unload or replacement in v1.
    /// </para>
    /// <para>
    /// The call performs synchronous file I/O and validation, so mount packs before the first tick
    /// when possible. It is thread-safe; mounting is atomic and a failed mount leaves the old stack
    /// untouched.
    /// </para>
    /// </remarks>
    void LoadPack(string packPath);

    /// <summary>
    /// Returns a materialized snapshot of the visible assets, one per canonical path.
    /// </summary>
    /// <param name="prefix">
    /// A canonical path prefix, or <see langword="null"/> for the whole tree. A non-null prefix
    /// matches the path equal to the prefix or paths under it on a <c>/</c> segment boundary
    /// (<c>sprites/pl</c> matches <c>sprites/pl</c> and <c>sprites/pl/idle</c>, but not
    /// <c>sprites/player</c>). An empty prefix is invalid.
    /// </param>
    /// <returns>
    /// A snapshot sorted by <see cref="AssetInfo.Path"/> ordinal with shadowed paths hidden. An
    /// empty filesystem or a prefix with no matches returns an empty list.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> is an invalid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// The list is safe to hold after a later mount, but it does not update. Do not call this per
    /// frame.
    /// </remarks>
    IReadOnlyList<AssetInfo> Browse(string? prefix = null);

    /// <summary>
    /// Tests whether the merged filesystem contains a canonical asset path.
    /// </summary>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <returns><see langword="true"/> when a visible entry has the exact path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="assetPath"/> is not a valid asset path.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The asset service has been disposed.</exception>
    /// <remarks>
    /// The lookup is exact, never a prefix match. A valid path that is absent returns
    /// <see langword="false"/> rather than throwing.
    /// </remarks>
    bool Exists(string assetPath);
}
