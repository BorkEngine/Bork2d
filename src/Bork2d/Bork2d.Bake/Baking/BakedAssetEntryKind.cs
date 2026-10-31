namespace Bork2d.Bake.Baking;

/// <summary>
/// The producer of one baked pack entry.
/// </summary>
internal enum BakedAssetEntryKind
{
    /// <summary>A bound custom-data asset serialized as MessagePack.</summary>
    CustomData,

    /// <summary>A processor's shaped output serialized as MessagePack.</summary>
    ProcessorAsset,

    /// <summary>A processor's raw data output.</summary>
    ProcessorData,
}
