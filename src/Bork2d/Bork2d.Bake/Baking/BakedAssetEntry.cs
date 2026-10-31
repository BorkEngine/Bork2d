using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;

namespace Bork2d.Bake.Baking;

/// <summary>
/// One entry of the complete baked asset set: its canonical path, kind, baked type name, producing
/// source, references, and source object.
/// </summary>
/// <remarks>
/// The index is built before anything is added to <c>PackBuilder</c>, so reference validation and
/// collision detection see the whole bake in one deterministic, sorted structure. A MessagePack entry
/// records the full name of the type the engine serializes; a raw entry has no type contract and
/// <see cref="TypeName"/> is <see langword="null"/>. Every kind exposes its reference sites and the
/// diagnostic display path of the source that produced it. The built-in texture processor produces
/// ordinary processor entries, so a texture descriptor is a <see cref="ProcessorAsset"/> entry.
/// </remarks>
internal sealed class BakedAssetEntry
{
    private BakedAssetEntry(
        string path,
        bool isRaw,
        string? typeName,
        BakedAssetEntryKind kind,
        BoundAsset? asset,
        BakeProcessorOutput? processorOutput,
        IReadOnlyList<BakeReferenceSite> references,
        string sourcePath)
    {
        Path = path;
        IsRaw = isRaw;
        TypeName = typeName;
        Kind = kind;
        Asset = asset;
        ProcessorOutput = processorOutput;
        References = references;
        SourcePath = sourcePath;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string Path { get; }

    /// <summary>Gets a value indicating whether the entry is a raw payload.</summary>
    internal bool IsRaw { get; }

    /// <summary>Gets the baked MessagePack type's full name, or <see langword="null"/> for a raw entry.</summary>
    internal string? TypeName { get; }

    /// <summary>Gets the producer of the entry.</summary>
    internal BakedAssetEntryKind Kind { get; }

    /// <summary>Gets the bound custom-data asset, or <see langword="null"/> for another kind.</summary>
    internal BoundAsset? Asset { get; }

    /// <summary>Gets the processor output, or <see langword="null"/> for another kind.</summary>
    internal BakeProcessorOutput? ProcessorOutput { get; }

    /// <summary>Gets the reference sites of this entry, in source traversal order.</summary>
    internal IReadOnlyList<BakeReferenceSite> References { get; }

    /// <summary>Gets the diagnostic display path of the source that produced the entry.</summary>
    internal string SourcePath { get; }

    /// <summary>Creates the MessagePack entry of a bound custom-data asset.</summary>
    /// <param name="asset">The bound asset.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForCustomData(BoundAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new BakedAssetEntry(
            asset.AssetPath,
            isRaw: false,
            asset.TypeName,
            BakedAssetEntryKind.CustomData,
            asset: asset,
            processorOutput: null,
            references: asset.References,
            sourcePath: asset.SourcePath);
    }

    /// <summary>Creates the MessagePack entry of a shaped processor output.</summary>
    /// <param name="output">The processor output.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForProcessorAsset(BakeProcessorOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (output.IsRaw)
        {
            throw new ArgumentException("The processor output is raw, not a shaped asset.", nameof(output));
        }

        return new BakedAssetEntry(
            output.Path,
            isRaw: false,
            output.TypeName,
            BakedAssetEntryKind.ProcessorAsset,
            asset: null,
            processorOutput: output,
            references: output.References,
            sourcePath: output.SourcePath);
    }

    /// <summary>Creates the raw entry of a processor data output.</summary>
    /// <param name="output">The processor output.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForProcessorData(BakeProcessorOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.IsRaw)
        {
            throw new ArgumentException("The processor output is a shaped asset, not raw data.", nameof(output));
        }

        return new BakedAssetEntry(
            output.Path,
            isRaw: true,
            typeName: null,
            BakedAssetEntryKind.ProcessorData,
            asset: null,
            processorOutput: output,
            references: output.References,
            sourcePath: output.SourcePath);
    }
}
