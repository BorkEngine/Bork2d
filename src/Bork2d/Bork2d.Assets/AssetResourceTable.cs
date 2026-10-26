using System.Diagnostics.CodeAnalysis;
using Bork2d.Api;

namespace Bork2d.Assets;

/// <summary>
/// The asset service's internal CPU resource table and sprite handle table.
/// </summary>
/// <remarks>
/// <para>
/// The table caches one texture-descriptor resource per canonical texture path, one sprite resource
/// per canonical sprite path, and one opaque <see cref="Sprite"/> handle per successfully resolved
/// sprite path. Resources are immutable after publication; the loader thread is the only writer of
/// resource entries. A failed descriptor or sprite is cached as failed and never retried, and a miss
/// does not allocate a handle (D-M3-12, D-M3-13, D-M3-18).
/// </para>
/// <para>
/// Reads take a short lock; no lock is held across a load, an await, or a blocking wait. There are
/// no weak references and no unload in v1: a cached resource stays for the process lifetime.
/// <see cref="LoadData"/> and <see cref="IAssets.LoadAsset{T}"/> never consult this table.
/// </para>
/// </remarks>
internal sealed class AssetResourceTable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SpriteHandleEntry> _handlesByPath =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, SpriteHandleEntry> _handlesById = [];
    private readonly Dictionary<string, TextureResource> _textures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteResource> _sprites =
        new(StringComparer.Ordinal);

    private int _nextHandleId = 1;

    /// <summary>Gets the number of cached sprite handles.</summary>
    internal int SpriteHandleCount
    {
        get
        {
            lock (_lock)
            {
                return _handlesByPath.Count;
            }
        }
    }

    /// <summary>Gets the number of cached texture resources, including failed ones.</summary>
    internal int TextureResourceCount
    {
        get
        {
            lock (_lock)
            {
                return _textures.Count;
            }
        }
    }

    /// <summary>Gets the number of cached sprite resources, including failed ones.</summary>
    internal int SpriteResourceCount
    {
        get
        {
            lock (_lock)
            {
                return _sprites.Count;
            }
        }
    }

    /// <summary>Looks up the cached handle entry for an already canonical sprite path.</summary>
    /// <param name="canonicalPath">The canonical sprite path.</param>
    /// <param name="entry">The cached entry, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a handle is cached for the path.</returns>
    internal bool TryGetHandle(
        string canonicalPath,
        [NotNullWhen(true)] out SpriteHandleEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        lock (_lock)
        {
            return _handlesByPath.TryGetValue(canonicalPath, out entry);
        }
    }

    /// <summary>
    /// Returns the cached handle entry for a path, allocating a new handle when none exists.
    /// </summary>
    /// <param name="canonicalPath">The canonical sprite path.</param>
    /// <param name="created">
    /// <see langword="true"/> when this call allocated the handle and the caller must enqueue the
    /// load; <see langword="false"/> when an existing handle (pending, ready, or failed) is reused.
    /// </param>
    /// <returns>The cached or newly allocated handle entry.</returns>
    /// <exception cref="InvalidOperationException">The handle id space is exhausted.</exception>
    internal SpriteHandleEntry GetOrAddHandle(string canonicalPath, out bool created)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        lock (_lock)
        {
            if (_handlesByPath.TryGetValue(canonicalPath, out SpriteHandleEntry? existing))
            {
                created = false;
                return existing;
            }

            int id = _nextHandleId;
            if (id <= 0)
            {
                throw new InvalidOperationException("The sprite handle id space is exhausted.");
            }

            _nextHandleId++;
            var entry = new SpriteHandleEntry(new Sprite(id), canonicalPath);
            _handlesByPath.Add(canonicalPath, entry);
            _handlesById.Add(id, entry);
            created = true;
            return entry;
        }
    }

    /// <summary>Looks up the cached handle entry for a handle.</summary>
    /// <param name="handle">The opaque sprite handle.</param>
    /// <param name="entry">The cached entry, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the handle belongs to this table.</returns>
    internal bool TryGetHandle(
        Sprite handle,
        [NotNullWhen(true)] out SpriteHandleEntry? entry)
    {
        lock (_lock)
        {
            return _handlesById.TryGetValue(handle.Id, out entry);
        }
    }

    /// <summary>Gets the internal completion task for a handle.</summary>
    /// <param name="handle">The opaque sprite handle.</param>
    /// <returns>The completion task; <see langword="true"/> on success, <see langword="false"/> on
    /// failure.</returns>
    /// <exception cref="ArgumentException">The handle is not a session-local sprite handle.</exception>
    internal Task<bool> GetCompletion(Sprite handle)
    {
        lock (_lock)
        {
            if (!_handlesById.TryGetValue(handle.Id, out SpriteHandleEntry? entry))
            {
                throw new ArgumentException(
                    "The sprite handle does not belong to this asset service.",
                    nameof(handle));
            }

            return entry.Completion;
        }
    }

    /// <summary>Looks up the cached texture-descriptor resource for a canonical texture path.</summary>
    /// <param name="canonicalPath">The canonical texture descriptor path.</param>
    /// <param name="resource">The resource, including a failed resource, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the path has a cached resource.</returns>
    internal bool TryGetTextureResource(
        string canonicalPath,
        [NotNullWhen(true)] out TextureResource? resource)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        lock (_lock)
        {
            return _textures.TryGetValue(canonicalPath, out resource);
        }
    }

    /// <summary>Caches a texture-descriptor resource. The first published resource wins.</summary>
    /// <param name="resource">The resource to cache.</param>
    internal void PublishTexture(TextureResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_lock)
        {
            _textures.TryAdd(resource.Path, resource);
        }
    }

    /// <summary>Looks up the cached sprite resource for a handle.</summary>
    /// <param name="handle">The opaque sprite handle.</param>
    /// <param name="resource">The resource, including a failed resource, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the handle's sprite path has a cached resource.</returns>
    internal bool TryGetSpriteResource(
        Sprite handle,
        [NotNullWhen(true)] out SpriteResource? resource)
    {
        lock (_lock)
        {
            resource = null;
            return _handlesById.TryGetValue(handle.Id, out SpriteHandleEntry? entry)
                && _sprites.TryGetValue(entry.Path, out resource);
        }
    }

    /// <summary>Caches a sprite resource. The first published resource wins.</summary>
    /// <param name="resource">The resource to cache.</param>
    internal void PublishSprite(SpriteResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_lock)
        {
            _sprites.TryAdd(resource.Path, resource);
        }
    }
}
