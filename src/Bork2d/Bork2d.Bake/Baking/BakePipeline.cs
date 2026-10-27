using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The complete multi-pack bake pipeline: discovery, per-pack custom-data binding, textures, baked
/// asset index, reference validation, pack composition, and the atomic pack writes.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BakeProject"/> declares one or more ordered packs. The first is the main/base pack;
/// the rest are DLC/overlay packs. Each pack is processed in order with its own root: sources are
/// discovered and bound through the project's explicit registrations, textures imported, its index
/// built, and its references validated. The main index is built first and is the only external index
/// a DLC pack receives; the main pack is strict (local entries only).
/// </para>
/// <para>
/// Validation always completes for every pack before <c>PackBuilder</c> writes anything: a source,
/// texture, collision, or reference failure returns a <see cref="BakeStatus.DiagnosticsError"/> result
/// and leaves every previous pack (and anything else in the destination) unchanged. Packs are
/// composed in memory after their own validation and written only after every pack has validated, so
/// a failed pack cannot corrupt an older pack. Each write is still atomic.
/// </para>
/// <para>
/// Duplicate canonical paths are legal across packs (overlay) and illegal inside one pack. The
/// pipeline is sequential and deterministic: packs in configuration order, discovery sorted, textures
/// sorted, indexes sorted, diagnostics through the deterministic sorter. <c>--check</c> stops after
/// validation and writes nothing.
/// </para>
/// <para>
/// The incremental manifest, skip/force behavior, and MSBuild integration are layered on top by WP5;
/// the pipeline itself always runs a full bake. WP1 keeps the existing texture pipeline as the only
/// texture producer; the registered processors are carried by the project for WP2.
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
        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(project, diagnostics);
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

        foreach (BakePackOutput output in outputs)
        {
            BakePack pack = output.Pack;
            IReadOnlyList<BakeSource> packSources = [.. sources
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

            TextureBakeResult textures = TexturePipeline.Import(project, pack);
            foreach (BakeDiagnostic diagnostic in textures.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            if (diagnostics.HasErrors)
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.Textures,
                    output.Path,
                    assets.Count,
                    textures.Textures.Count,
                    referenceCount: 0,
                    diagnostics);
            }

            int referenceCount = 0;
            foreach (BoundAsset asset in assets)
            {
                referenceCount += asset.References.Count;
            }

            BakedAssetIndex index = BakedAssetIndex.Build(
                assets,
                textures.Textures,
                project.ProjectRoot,
                diagnostics,
                pack.PackPath);
            if (diagnostics.HasErrors)
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.References,
                    output.Path,
                    assets.Count,
                    textures.Textures.Count,
                    referenceCount,
                    diagnostics);
            }

            BakedAssetIndex? externalIndex = pack.IsMain ? null : mainIndex;
            if (!BakeReferenceValidator.Validate(
                assets,
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
                    assets.Count,
                    textures.Textures.Count,
                    referenceCount,
                    diagnostics);
            }

            PackBuilder? builder = null;
            if (!options.Check)
            {
                builder = PackComposer.Compose(
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
                        assets.Count,
                        textures.Textures.Count,
                        referenceCount,
                        diagnostics);
                }
            }

            if (pack.IsMain)
            {
                mainIndex = index;
            }

            packStates.Add(new PackState(
                output,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                index,
                builder));
        }

        if (options.Check)
        {
            return Success(
                packStates,
                packWritten: false,
                diagnostics);
        }

        // Every pack validated before the first write, so an invalid DLC pack cannot leave a
        // half-updated pack set. Each individual write is still atomic.
        foreach (PackState state in packStates)
        {
            PackBuilder builder = state.Builder
                ?? throw new InvalidOperationException("A non-check bake produced no pack builder.");
            if (!AtomicPackWriter.Write(state.Output.Path, builder, diagnostics))
            {
                return Failure(
                    BakeStatus.DiagnosticsError,
                    BakeFailureStage.Pack,
                    state.Output.Path,
                    state.AssetCount,
                    state.TextureCount,
                    state.ReferenceCount,
                    diagnostics);
            }
        }

        return Success(packStates, packWritten: true, diagnostics);
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
        BakedAssetIndex Index,
        PackBuilder? Builder);
}
