using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The outcome of baking one pack: its configured pack, output path, counts, and entries.
/// </summary>
/// <remarks>
/// The main pack is the first configured pack; every later entry is a DLC/overlay pack.
/// <see cref="PackWritten"/> is false for a <c>--check</c> run.
/// </remarks>
internal sealed class BakedPackResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakedPackResult"/> class.
    /// </summary>
    /// <param name="pack">The configured pack the result describes.</param>
    /// <param name="packPath">The absolute pack output path.</param>
    /// <param name="packWritten">Whether the pack file was written.</param>
    /// <param name="assetCount">
    /// The number of shaped assets in the pack: bound custom data plus processor outputs emitted with
    /// <c>AddAsset&lt;T&gt;</c>.
    /// </param>
    /// <param name="textureCount">The number of baked textures in the pack.</param>
    /// <param name="referenceCount">The number of reference sites validated in the pack.</param>
    /// <param name="entries">The pack's complete baked entry set, in ordinal path order.</param>
    internal BakedPackResult(
        BakePack pack,
        string packPath,
        bool packWritten,
        int assetCount,
        int textureCount,
        int referenceCount,
        IReadOnlyList<BakedAssetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentNullException.ThrowIfNull(entries);

        Pack = pack;
        PackPath = packPath;
        PackWritten = packWritten;
        AssetCount = assetCount;
        TextureCount = textureCount;
        ReferenceCount = referenceCount;
        Entries = [.. entries];
    }

    /// <summary>Gets the configured pack the result describes.</summary>
    internal BakePack Pack { get; }

    /// <summary>Gets a value indicating whether this is the main/base pack.</summary>
    internal bool IsMain => Pack.IsMain;

    /// <summary>Gets the absolute pack output path.</summary>
    internal string PackPath { get; }

    /// <summary>Gets a value indicating whether the pack file was written.</summary>
    internal bool PackWritten { get; }

    /// <summary>
    /// Gets the number of shaped assets in the pack: bound custom data plus processor outputs emitted
    /// with <c>AddAsset&lt;T&gt;</c>.
    /// </summary>
    internal int AssetCount { get; }

    /// <summary>Gets the number of baked textures in the pack.</summary>
    internal int TextureCount { get; }

    /// <summary>Gets the number of reference sites validated in the pack.</summary>
    internal int ReferenceCount { get; }

    /// <summary>Gets the pack's complete baked entry set in ordinal path order.</summary>
    internal IReadOnlyList<BakedAssetEntry> Entries { get; }
}

/// <summary>
/// The outcome of one bake pipeline run: its status, where it stopped, the per-pack results, aggregate
/// counts, the composed entry set, and the deterministically ordered diagnostics.
/// </summary>
/// <remarks>
/// A <see cref="BakeStatus.Success"/> result with <see cref="PackWritten"/> false is a
/// <c>--check</c>/<c>validate</c> run. A <see cref="BakeStatus.UpToDate"/> result means the
/// incremental manifest matched: no bake ran, the existing packs are current, and the counts are the
/// recorded ones. <see cref="PackPath"/> is the main pack's path. A failed result never has entries or
/// a written pack.
/// </remarks>
internal sealed class BakeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeResult"/> class.
    /// </summary>
    /// <param name="status">The run classification.</param>
    /// <param name="failureStage">Where the run stopped.</param>
    /// <param name="packPath">The main pack target, when known.</param>
    /// <param name="packWritten">Whether every pack was written.</param>
    /// <param name="assetCount">
    /// The aggregate number of shaped assets: bound custom data plus processor outputs emitted with
    /// <c>AddAsset&lt;T&gt;</c>.
    /// </param>
    /// <param name="textureCount">The aggregate number of baked textures.</param>
    /// <param name="referenceCount">
    /// The aggregate number of reference sites across every entry in every pack index (custom data and
    /// processor shaped output).
    /// </param>
    /// <param name="packs">The per-pack results, in pack order.</param>
    /// <param name="entries">The aggregate baked entry set, or empty when the run failed early.</param>
    /// <param name="diagnostics">The sorted diagnostics.</param>
    internal BakeResult(
        BakeStatus status,
        BakeFailureStage failureStage,
        string? packPath,
        bool packWritten,
        int assetCount,
        int textureCount,
        int referenceCount,
        IReadOnlyList<BakedPackResult> packs,
        IReadOnlyList<BakedAssetEntry> entries,
        IReadOnlyList<BakeDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Status = status;
        FailureStage = failureStage;
        PackPath = packPath;
        PackWritten = packWritten;
        AssetCount = assetCount;
        TextureCount = textureCount;
        ReferenceCount = referenceCount;
        Packs = [.. packs];
        Entries = [.. entries];
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the run classification.</summary>
    internal BakeStatus Status { get; }

    /// <summary>Gets where the run stopped.</summary>
    internal BakeFailureStage FailureStage { get; }

    /// <summary>Gets the main pack target, when known.</summary>
    internal string? PackPath { get; }

    /// <summary>Gets a value indicating whether every pack was written.</summary>
    internal bool PackWritten { get; }

    /// <summary>
    /// Gets the aggregate number of shaped assets: bound custom data plus processor outputs emitted
    /// with <c>AddAsset&lt;T&gt;</c>.
    /// </summary>
    internal int AssetCount { get; }

    /// <summary>Gets the aggregate number of baked textures.</summary>
    internal int TextureCount { get; }

    /// <summary>Gets the aggregate number of reference sites validated.</summary>
    internal int ReferenceCount { get; }

    /// <summary>Gets the per-pack results, in pack order.</summary>
    internal IReadOnlyList<BakedPackResult> Packs { get; }

    /// <summary>Gets the aggregate baked entry set in ordinal path order.</summary>
    internal IReadOnlyList<BakedAssetEntry> Entries { get; }

    /// <summary>Gets the diagnostics in the deterministic order the diagnostics model defines.</summary>
    internal IReadOnlyList<BakeDiagnostic> Diagnostics { get; }
}
