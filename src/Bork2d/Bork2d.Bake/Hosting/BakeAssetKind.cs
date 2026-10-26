namespace Bork2d.Bake.Hosting;

/// <summary>
/// The kind of pack entry a mapped type produces.
/// </summary>
internal enum BakeAssetKind
{
    /// <summary>A MessagePack asset produced from a generated shape.</summary>
    MessagePack,

    /// <summary>An opaque raw payload produced by the tooling (for example a texture payload).</summary>
    Raw,
}
