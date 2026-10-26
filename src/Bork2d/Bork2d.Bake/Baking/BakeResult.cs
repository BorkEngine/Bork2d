using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The outcome of one bake pipeline run: its status, where it stopped, counts, the composed entry
/// set, and the deterministically ordered diagnostics.
/// </summary>
/// <remarks>
/// A <see cref="BakeStatus.Success"/> result with <see cref="PackWritten"/> false is a
/// <c>--check</c>/<c>validate</c> run. A <see cref="BakeStatus.UpToDate"/> result means the
/// incremental manifest matched: no bake ran, the existing pack is current, and the counts are the
/// recorded ones. The pack path is reported when it is known, so the caller can show the target that
/// was (or would be) written. A failed result never has entries or a written pack.
/// </remarks>
internal sealed class BakeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeResult"/> class.
    /// </summary>
    /// <param name="status">The run classification.</param>
    /// <param name="failureStage">Where the run stopped.</param>
    /// <param name="packPath">The absolute pack target, when known.</param>
    /// <param name="packWritten">Whether the pack was written.</param>
    /// <param name="assetCount">The number of bound custom-data assets.</param>
    /// <param name="textureCount">The number of baked textures.</param>
    /// <param name="referenceCount">The number of reference sites validated.</param>
    /// <param name="entries">The complete baked entry set, or empty when the run failed early.</param>
    /// <param name="diagnostics">The sorted diagnostics.</param>
    internal BakeResult(
        BakeStatus status,
        BakeFailureStage failureStage,
        string? packPath,
        bool packWritten,
        int assetCount,
        int textureCount,
        int referenceCount,
        IReadOnlyList<BakedAssetEntry> entries,
        IReadOnlyList<BakeDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Status = status;
        FailureStage = failureStage;
        PackPath = packPath;
        PackWritten = packWritten;
        AssetCount = assetCount;
        TextureCount = textureCount;
        ReferenceCount = referenceCount;
        Entries = [.. entries];
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the run classification.</summary>
    internal BakeStatus Status { get; }

    /// <summary>Gets where the run stopped.</summary>
    internal BakeFailureStage FailureStage { get; }

    /// <summary>Gets the absolute pack target, when known.</summary>
    internal string? PackPath { get; }

    /// <summary>Gets a value indicating whether a pack was written.</summary>
    internal bool PackWritten { get; }

    /// <summary>Gets the number of bound custom-data assets.</summary>
    internal int AssetCount { get; }

    /// <summary>Gets the number of baked textures.</summary>
    internal int TextureCount { get; }

    /// <summary>Gets the number of reference sites validated.</summary>
    internal int ReferenceCount { get; }

    /// <summary>Gets the complete baked entry set in ordinal path order.</summary>
    internal IReadOnlyList<BakedAssetEntry> Entries { get; }

    /// <summary>Gets the diagnostics in the deterministic order the diagnostics model defines.</summary>
    internal IReadOnlyList<BakeDiagnostic> Diagnostics { get; }
}
