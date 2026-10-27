using Bork2d.Assets.Pack;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Project;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The complete bake pipeline: descriptor, generated host, custom-data binding, textures, the baked
/// asset index, reference validation, pack composition, and the atomic pack write.
/// </summary>
/// <remarks>
/// <para>
/// The order is the M4 §5.1/§9 contract. Validation always completes before <c>PackBuilder</c> is
/// touched: a source, texture, collision, or reference failure returns a
/// <see cref="BakeStatus.DiagnosticsError"/> result and leaves the previous pack (and anything else in
/// the destination) unchanged. Only a fully valid bake composes and atomically replaces the pack.
/// </para>
/// <para>
/// The pipeline is sequential and deterministic (Q-25): source discovery is sorted, textures are
/// sorted by canonical path, the index is sorted, and diagnostics are returned through the
/// deterministic sorter. <c>--check</c> stops after validation and writes nothing.
/// </para>
/// <para>
/// The incremental manifest, skip/force behavior, and MSBuild integration are layered on top by
/// <c>BakeIncrementalPlanner</c> (WP5); the pipeline itself always runs a full bake.
/// </para>
/// </remarks>
internal static class BakePipeline
{
    /// <summary>Runs one full or check-only bake.</summary>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The result, including counts and the deterministically ordered diagnostics.</returns>
    internal static BakeResult Run(BakeOptions options, BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(
            options.ProjectPath,
            diagnostics);
        if (descriptor is null || diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.ConfigurationError,
                BakeFailureStage.Descriptor,
                packPath: null,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        string packPath = options.OutputPath ?? descriptor.PackPath;

        IReadOnlyList<BakeSource> sources = BakeSourceDiscovery.Discover(descriptor, diagnostics);
        if (diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Sources,
                packPath,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        BakeHostHandle? handle = BakeHostLoader.TryLoad(options.HostPath, diagnostics);
        if (handle is null)
        {
            return Failure(
                BakeStatus.ConfigurationError,
                BakeFailureStage.Host,
                packPath,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        if (!BakeHostManifestValidator.Validate(descriptor, sources, handle.Manifest, diagnostics)
            || diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.ConfigurationError,
                BakeFailureStage.Host,
                packPath,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        IReadOnlyList<BoundAsset> assets = BakeSourceBinder.BindAll(
            descriptor,
            sources,
            handle.Manifest,
            diagnostics);
        if (diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Sources,
                packPath,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                diagnostics);
        }

        TextureBakeResult textures = TexturePipeline.Import(descriptor);
        foreach (BakeDiagnostic diagnostic in textures.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        if (diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Textures,
                packPath,
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
            descriptor.DescriptorDirectory,
            diagnostics);
        if (diagnostics.HasErrors)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.References,
                packPath,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                diagnostics);
        }

        if (!BakeReferenceValidator.Validate(assets, index, diagnostics))
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.References,
                packPath,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                diagnostics);
        }

        if (options.Check)
        {
            return new BakeResult(
                BakeStatus.Success,
                BakeFailureStage.None,
                packPath,
                packWritten: false,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                index.Entries,
                diagnostics.GetSorted());
        }

        PackBuilder? builder = PackComposer.Compose(
            index,
            handle.Manifest,
            options.Mode.ToCompressionMode(),
            diagnostics);
        if (builder is null)
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Pack,
                packPath,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                diagnostics);
        }

        if (!AtomicPackWriter.Write(packPath, builder, diagnostics))
        {
            return Failure(
                BakeStatus.DiagnosticsError,
                BakeFailureStage.Pack,
                packPath,
                assets.Count,
                textures.Textures.Count,
                referenceCount,
                diagnostics);
        }

        return new BakeResult(
            BakeStatus.Success,
            BakeFailureStage.None,
            packPath,
            packWritten: true,
            assets.Count,
            textures.Textures.Count,
            referenceCount,
            index.Entries,
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
            entries: [],
            diagnostics.GetSorted());
}
