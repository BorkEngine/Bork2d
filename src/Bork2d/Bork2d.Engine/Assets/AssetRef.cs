using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A typed reference to a MessagePack asset.
/// </summary>
/// <typeparam name="TAsset">
/// The referenced asset type, a class or a struct. It must have a compiled
/// <c>[GenerateShape]</c> shape so it can be loaded through
/// <see cref="IAssets.LoadAsset{T}(string)"/>.
/// </typeparam>
/// <param name="Path">
/// The canonical asset path, or <see langword="null"/> when the reference is absent.
/// </param>
/// <remarks>
/// <para>
/// References are stored as canonical path strings; they never hold a loaded asset or a session
/// handle. Resolve a reference with <see cref="IAssets.LoadAsset{T}(AssetRef{TAsset})"/> or by
/// passing <see cref="Path"/> to <see cref="IAssets.LoadAsset{T}(string)"/>.
/// </para>
/// <para>
/// A <see langword="null"/> path means the reference is absent and is valid. An empty or
/// non-canonical path is invalid and is rejected when a pack is built.
/// </para>
/// </remarks>
[TypeShape(Marshaler = typeof(AssetRefMarshaler<>))]
public readonly record struct AssetRef<TAsset>(string? Path) : IAssetReference
    where TAsset : IShapeable<TAsset>
{
    /// <summary>
    /// Gets the absent reference.
    /// </summary>
    public static AssetRef<TAsset> None => default;

    /// <summary>
    /// Gets a value indicating whether <see cref="Path"/> is present.
    /// </summary>
    public bool HasValue => Path is not null;

    /// <summary>
    /// Converts a canonical asset path to a typed reference.
    /// </summary>
    /// <param name="path">The canonical asset path, or <see langword="null"/> for no reference.</param>
    public static implicit operator AssetRef<TAsset>(string? path) => new(path);

    /// <inheritdoc/>
    string? IAssetReference.Path => Path;

    /// <inheritdoc/>
    bool IAssetReference.IsData => false;

    /// <inheritdoc/>
    string? IAssetReference.TargetTypeName => typeof(TAsset).FullName;
}
