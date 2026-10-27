namespace Bork2d.Bake.Baking;

/// <summary>
/// The pipeline that produced one baked pack entry.
/// </summary>
internal enum BakedAssetEntryKind
{
    /// <summary>A bound custom-data asset serialized as MessagePack.</summary>
    CustomData,

    /// <summary>A texture's <c>TextureDescriptor</c> serialized as MessagePack.</summary>
    TextureDescriptor,

    /// <summary>A texture's raw RGBA8 payload.</summary>
    TexturePayload,

    /// <summary>A processor's shaped output serialized as MessagePack.</summary>
    ProcessorAsset,

    /// <summary>A processor's raw data output.</summary>
    ProcessorData,
}
