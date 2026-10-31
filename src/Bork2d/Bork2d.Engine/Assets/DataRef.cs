using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A reference to an opaque raw asset.
/// </summary>
/// <param name="Path">
/// The canonical asset path, or <see langword="null"/> when the reference is absent.
/// </param>
/// <remarks>
/// <para>
/// References are stored as canonical path strings. Resolve one with
/// <see cref="IAssets.LoadData(DataRef)"/> or by passing <see cref="Path"/> to
/// <see cref="IAssets.LoadData(string)"/>.
/// </para>
/// <para>
/// A <see langword="null"/> path means the reference is absent and is valid. An empty or
/// non-canonical path is invalid and is rejected when a pack is built.
/// </para>
/// </remarks>
[TypeShape(Marshaler = typeof(DataRefMarshaler))]
public readonly record struct DataRef(string? Path) : IAssetReference
{
    /// <summary>
    /// Gets the absent reference.
    /// </summary>
    public static DataRef None => default;

    /// <summary>
    /// Gets a value indicating whether <see cref="Path"/> is present.
    /// </summary>
    public bool HasValue => Path is not null;

    /// <summary>
    /// Converts a canonical asset path to a raw-data reference.
    /// </summary>
    /// <param name="path">The canonical asset path, or <see langword="null"/> for no reference.</param>
    public static implicit operator DataRef(string? path) => new(path);

    /// <inheritdoc/>
    string? IAssetReference.Path => Path;

    /// <inheritdoc/>
    bool IAssetReference.IsData => true;

    /// <inheritdoc/>
    string? IAssetReference.TargetTypeName => null;
}
