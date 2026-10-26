using System.ComponentModel;
using PolyType;

namespace Bork2d.Api.Assets;

/// <summary>
/// Serializes <see cref="AssetRef{TAsset}"/> as its canonical path string.
/// </summary>
/// <typeparam name="TAsset">The referenced asset type.</typeparam>
/// <remarks>
/// This type is public because PolyType generates the closed shape for
/// <c>AssetRef&lt;TAsset&gt;</c> in the assembly that declares the containing asset type. It is
/// infrastructure, not a game-facing API.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AssetRefMarshaler<TAsset> : IMarshaler<AssetRef<TAsset>, string?>
    where TAsset : class, IShapeable<TAsset>
{
    /// <inheritdoc/>
    public string? Marshal(AssetRef<TAsset> value) => value.Path;

    /// <inheritdoc/>
    public AssetRef<TAsset> Unmarshal(string? surrogate) => new(surrogate);
}
