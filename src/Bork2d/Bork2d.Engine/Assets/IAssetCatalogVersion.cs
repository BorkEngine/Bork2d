namespace Bork2d.Engine.Assets;

/// <summary>
/// The internal monotonic identity of the visible asset catalog.
/// </summary>
/// <remarks>
/// A mount publishes a new immutable overlay snapshot, so a canonical path can resolve to different
/// content without changing its path. The renderer's texture-page cache observes this value to
/// retire pages whose content predates the latest mount. It is an internal seam, not part of the
/// public asset contract; <c>AssetService</c> implements it from the mounted pack count, which never
/// decreases because there is no unload. The value follows the asset service lifetime: the renderer
/// joins before the service stops, so a render-thread read is valid.
/// </remarks>
internal interface IAssetCatalogVersion
{
    /// <summary>Gets the current catalog version; it changes exactly when a pack is mounted.</summary>
    long CatalogVersion { get; }
}
