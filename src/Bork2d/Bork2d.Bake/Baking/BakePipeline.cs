using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Sources;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The complete multi-pack bake pipeline: discovery, per-pack custom-data binding and validation,
/// processor execution, the generic baked asset index, reference validation, pack composition and
/// staging, and the transactional publish of the whole pack set.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BakeProject"/> declares one or more ordered packs. The first is the main/base pack;
/// the rest are DLC/overlay packs. Each pack is processed in order with its own root: sources are
/// discovered and bound through the project's explicit registrations, every registered validator for
/// each bound asset runs, registered processors run over their non-TOML sources (the built-in texture
/// processor is one of them), its index is built, and its references are validated. The main index is
/// built first and is the only external index a DLC pack receives; the main pack is strict (local
/// entries only). A validator error is a <see cref="BakeFailureStage.Validation"/> failure.
/// </para>
/// <para>
/// Every pack validates before any target is touched. After a pack validates it is composed into a
/// <c>PackBuilder</c> and immediately staged in a unique temporary file next to its target; the builder
/// is released as soon as the pack is staged. The staged set is published only after every pack has
/// validated and staged, through a <see cref="PackPublishTransaction"/> that replaces each target via
/// its own backup and rolls every already-replaced target back on failure. A normal source, validation,
/// processor, reference, composition, staging, or publish failure therefore leaves every previous pack
/// byte-identical. A process crash inside the publish window cannot be rolled back; see
/// <see cref="PackPublishTransaction"/>.
/// </para>
/// <para>
/// Staged packs live on disk and the per-pack state keeps only the index and counts, so exactly one
/// <c>PackBuilder</c> (and its composed payloads) is alive at a time regardless of the number or size
/// of the configured packs.
/// </para>
/// <para>
/// Duplicate canonical paths are legal across packs (overlay) and illegal inside one pack. The
/// pipeline is sequential and deterministic: packs in configuration order, discovery sorted, processor
/// outputs sorted, indexes sorted, diagnostics through the deterministic sorter. <c>--check</c> stops
/// after validation, composes nothing, and writes no pack, staging file, or source-tree change.
/// </para>
/// <para>
/// The result's texture count is the number of shaped processor outputs whose type is
/// <see cref="TextureDescriptor"/>; the asset count is custom data plus the other shaped processor
/// outputs. There is no texture-specific pipeline or index branch.
/// </para>
/// <para>
/// The incremental manifest, skip/force behavior, and MSBuild integration are layered on top by WP5;
/// the pipeline itself always runs a full bake.
/// </para>
/// </remarks>
internal static class BakePipeline
{
    /// <summary>Runs one full or check-only bake of every configured pack.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The result, including per-pack counts and the deterministically ordered diagnostics.</returns>
    internal static BakeResult Run(
        BakeProject project,
        BakeOptions options,
        BakeDiagnostics diagnostics)
        => Run(project, options, diagnostics, stagingProbe: null);

    /// <summary>
    /// Runs one full or check-only bake of every configured pack with an internal staging probe.
    /// </summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="stagingProbe">
    /// An optional test-only observer of the post-staging state; production callers pass
    /// <see langword="null"/>.
    /// </param>
    /// <returns>The result, including per-pack counts and the deterministically ordered diagnostics.</returns>
    internal static BakeResult Run(
        BakeProject project,
        BakeOptions options,
        BakeDiagnostics diagnostics,
        IPackStagingProbe? stagingProbe)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!BakeOutputLayout.TryResolve(
            project,
            options.OutputRoot,
            diagnostics,
            out _,
            out IReadOnlyList<BakePackOutput> outputs))
        {
            return Failure(
                BakeStatus.ConfigurationError,
                BakeFailureStage.Configuration,
                packPath: null,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        string mainPackPath = outputs[0].Path;
        BakeDiscoveredSources discovered = BakeSourceDiscovery.Discover(project, diagnostics);
        if (diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Sources,
                mainPackPath,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        var packStates = new List<PackState>(outputs.Count);
        BakedAssetIndex? mainIndex = null;

        // A check validates and composes nothing; every other run stages each pack as soon as it
        // validates and keeps the staged set until every pack has been staged.
        using PackPublishTransaction? transaction = options.Check ? null : new();

        foreach (BakePackOutput output in outputs)
        {
            BakePack pack = output.Pack;
            IReadOnlyList<BakeSource> packSources = [.. discovered.DataSources
                .Where(source => source.Pack.Index == pack.Index)];

            IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
                project,
                packSources,
                diagnostics);
            if (diagnostics.HasErrors)
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.Sources,
                    output.Path,
                    assetCount: assets.Count,
                    textureCount: 0,
                    referenceCount: 0,
                    diagnostics);
            }

            if (!RunValidators(project, assets, diagnostics))
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.Validation,
                    output.Path,
                    assetCount: assets.Count,
                    textureCount: 0,
                    referenceCount: 0,
                    diagnostics);
            }

            IReadOnlyList<BakeProcessorSource> packProcessorSources = [.. discovered.ProcessorSources
                .Where(source => source.Pack.Index == pack.Index)];
            IReadOnlyList<BakeProcessorOutput> processorOutputs = BakeProcessorRunner.Run(
                project,
                packProcessorSources,
                diagnostics,
                allowSourceWrites: !options.Check);
            int textureCount = CountTextureOutputs(processorOutputs);
            int assetCount = assets.Count + CountShapedOutputs(processorOutputs) - textureCount;
            int referenceCount = CountReferences(assets, processorOutputs);

            if (diagnostics.HasErrors)
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.Processors,
                    output.Path,
                    assetCount,
                    textureCount,
                    referenceCount,
                    diagnostics);
            }

            BakedAssetIndex index = BakedAssetIndex.Build(
                assets,
                processorOutputs,
                diagnostics,
                pack.PackPath);
            if (diagnostics.HasErrors)
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.References,
                    output.Path,
                    assetCount,
                    textureCount,
                    referenceCount,
                    diagnostics);
            }

            referenceCount = CountReferences(index);
            BakedAssetIndex? externalIndex = pack.IsMain ? null : mainIndex;
            if (!BakeReferenceValidator.Validate(
                index,
                externalIndex,
                pack.PackPath,
                diagnostics,
                project.MainPack.PackPath))
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.References,
                    output.Path,
                    assetCount,
                    textureCount,
                    referenceCount,
                    diagnostics);
            }

            if (!options.Check)
            {
                PackBuilder? builder = PackComposer.Compose(
                    index,
                    project,
                    options.Mode.ToCompressionMode(),
                    diagnostics,
                    externalIndex);
                if (builder is null)
                {
                    return Failure(
                        BakeStatus.DiagnosticsError,
                        BakeFailureStage.Pack,
                        output.Path,
                        assetCount,
                        textureCount,
                        referenceCount,
                        diagnostics);
                }

                if (!transaction!.Stage(output.Path, builder, diagnostics))
                {
                    return Failure(
                        BakeStatus.DiagnosticsError,
                        BakeFailureStage.Pack,
                        output.Path,
                        assetCount,
                        textureCount,
                        referenceCount,
                        diagnostics);
                }

                // Staged bytes are on disk now; drop the builder so composing the next pack cannot
                // keep this pack's payloads alive.
                builder = null;
                stagingProbe?.OnPackStaged(pack.Index, transaction.StagedPackCount, builder);
            }

            if (pack.IsMain)
            {
                mainIndex = index;
            }

            packStates.Add(new PackState(
                output,
                assetCount,
                textureCount,
                referenceCount,
                index));
        }

        if (options.Check)
        {
            return Success(
                packStates,
                packWritten: false,
                diagnostics);
        }

        // Every pack validated and staged before the first target is touched, so a failed pack cannot
        // leave a half-updated pack set. A publish failure rolls every replaced target back.
        if (!transaction!.Commit(diagnostics, out int failedPackIndex))
        {
            PackState failed = packStates[failedPackIndex];
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Pack,
                failed.Output.Path,
                failed.AssetCount,
                failed.TextureCount,
                failed.ReferenceCount,
                diagnostics);
        }

        return Success(packStates, packWritten: true, diagnostics);
    }

    private static bool RunValidators(
        BakeProject project,
        IReadOnlyList<BoundAsset> assets,
        BakeDiagnostics diagnostics)
    {
        bool valid = true;

        foreach (BoundAsset asset in assets)
        {
            if (!project.AssetTypes.TryGetValue(
                    asset.TypeName,
                    out BakeAssetTypeRegistration? registration)
                || registration.Validators.Count == 0)
            {
                continue;
            }

            foreach (IBakeAssetValidator validator in registration.Validators)
            {
                var sink = new BakeProcessorDiagnosticSink(
                    validator.Name,
                    asset.SourcePath,
                    asset.AssetPath,
                    diagnostics);
                validator.Validate(asset.AssetPath, asset.Value, sink);
                if (sink.HasError)
                {
                    valid = false;
                }
            }
        }

        return valid;
    }

    private static int CountShapedOutputs(IReadOnlyList<BakeProcessorOutput> outputs)
    {
        int count = 0;
        foreach (BakeProcessorOutput output in outputs)
        {
            if (!output.IsRaw)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountTextureOutputs(IReadOnlyList<BakeProcessorOutput> outputs)
    {
        string? textureTypeName = typeof(TextureDescriptor).FullName;
        int count = 0;
        foreach (BakeProcessorOutput output in outputs)
        {
            if (!output.IsRaw
                && string.Equals(output.TypeName, textureTypeName, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountReferences(
        IReadOnlyList<BoundAsset> assets,
        IReadOnlyList<BakeProcessorOutput> outputs)
    {
        int count = 0;
        foreach (BoundAsset asset in assets)
        {
            count += asset.References.Count;
        }

        foreach (BakeProcessorOutput output in outputs)
        {
            count += output.References.Count;
        }

        return count;
    }

    private static int CountReferences(BakedAssetIndex index)
    {
        int count = 0;
        foreach (BakedAssetEntry entry in index.Entries)
        {
            count += entry.References.Count;
        }

        return count;
    }

    private static BakeResult Success(
        IReadOnlyList<PackState> states,
        bool packWritten,
        BakeDiagnostics diagnostics)
    {
        var packs = new List<BakedPackResult>(states.Count);
        var entries = new List<BakedAssetEntry>();
        int assetCount = 0;
        int textureCount = 0;
        int referenceCount = 0;

        foreach (PackState state in states)
        {
            packs.Add(new BakedPackResult(
                state.Output.Pack,
                state.Output.Path,
                packWritten,
                state.AssetCount,
                state.TextureCount,
                state.ReferenceCount,
                state.Index.Entries));
            entries.AddRange(state.Index.Entries);
            assetCount += state.AssetCount;
            textureCount += state.TextureCount;
            referenceCount += state.ReferenceCount;
        }

        return new BakeResult(
            BakeStatus.Success,
            BakeFailureStage.None,
            packs[0].PackPath,
            packWritten,
            assetCount,
            textureCount,
            referenceCount,
            packs,
            entries,
            diagnostics.GetSorted());
    }

    private static BakeResult Failure(
        BakeStatus status,
        BakeFailureStage stage,
        string? packPath,
        int assetCount,
        int textureCount,
        int referenceCount,
        BakeDiagnostics diagnostics)
        => new(
            status,
            stage,
            packPath,
            packWritten: false,
            assetCount,
            textureCount,
            referenceCount,
            packs: [],
            entries: [],
            diagnostics.GetSorted());

    private sealed record PackState(
        BakePackOutput Output,
        int AssetCount,
        int TextureCount,
        int ReferenceCount,
        BakedAssetIndex Index);
}
