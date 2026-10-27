using System.Diagnostics.CodeAnalysis;
using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Decides whether a <c>bake</c> invocation can skip the pipeline and records the schema-3 manifest
/// after a successful full bake.
/// </summary>
/// <remarks>
/// <para>
/// <c>--check</c>/<c>validate</c> always run <see cref="BakePipeline.Run"/>: a check must validate the
/// current sources even when a manifest matches, and it never writes pack or manifest state.
/// Otherwise the planner collects the current inputs and compares the tool version, configuration
/// hash, shape-closure hash, mode, every tracked input path/hash, and every output path/hash against
/// a readable schema-3 manifest. A match with every output present and unchanged returns
/// <see cref="BakeStatus.UpToDate"/> with an informational <c>BK5005</c> and leaves the packs and
/// manifest untouched.
/// </para>
/// <para>
/// A missing, corrupt, or mismatched manifest, any changed source, and <c>--force</c> run the full
/// pipeline. A successful bake then writes the manifest atomically; if that write fails after the
/// packs are valid it is a <c>BK5003</c> warning and the bake still succeeds, so the next invocation
/// simply bakes again. A failed bake writes neither packs nor manifest.
/// </para>
/// </remarks>
internal static class BakeIncrementalPlanner
{
    /// <summary>Runs one incremental-aware bake.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The bake or up-to-date result.</returns>
    internal static BakeResult Run(
        BakeProject project,
        BakeOptions options,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options.Check)
        {
            return BakePipeline.Run(project, options, diagnostics);
        }

        string manifestPath = BakeManifestPaths.ManifestPath(project.ProjectRoot);
        BakeIncrementalInputs? inputs = BakeIncrementalInputs.TryCollect(project, options);

        if (!options.Force && inputs is not null)
        {
            BakeIncrementalManifest? manifest = BakeManifestStore.TryRead(manifestPath);
            if (manifest is not null && IsUpToDate(manifest, inputs))
            {
                diagnostics.Add(
                    BakeSeverity.Info,
                    BakeDiagnosticCodes.IncrementalSkipped,
                    "The bake is up to date: every tracked input, the configuration, the registered "
                        + "type/processor assembly closure, the mode, and every output hash match the "
                        + "incremental manifest; no pack was written.",
                    manifestPath);

                return UpToDateResult(project, manifest, diagnostics);
            }
        }

        BakeResult result = BakePipeline.Run(project, options, diagnostics);
        if (result.Status != BakeStatus.Success || !result.PackWritten || inputs is null)
        {
            return result;
        }

        // A full bake may create missing Required sidecars, and every sidecar is a tracked input.
        // Re-collect after the bake so the manifest records the post-bake tree; otherwise the next
        // bake would see a new `.toml` input and run one extra full bake instead of skipping.
        BakeIncrementalInputs? recordedInputs = BakeIncrementalInputs.TryCollect(project, options);
        if (recordedInputs is null)
        {
            diagnostics.Add(
                BakeSeverity.Warning,
                BakeDiagnosticCodes.IncrementalManifestWriteFailed,
                $"The bake succeeded, but its tracked inputs could not be re-collected for the "
                    + $"incremental manifest '{manifestPath}'; the next bake runs in full.",
                manifestPath);
            return WithUpdatedDiagnostics(result, diagnostics);
        }

        if (!TryCreateManifest(recordedInputs, result, out BakeIncrementalManifest? written))
        {
            diagnostics.Add(
                BakeSeverity.Warning,
                BakeDiagnosticCodes.IncrementalManifestWriteFailed,
                $"The bake succeeded, but its output hashes could not be recorded in the incremental "
                    + $"manifest '{manifestPath}'; the next bake runs in full.",
                manifestPath);
            return WithUpdatedDiagnostics(result, diagnostics);
        }

        if (!BakeManifestStore.TryWrite(manifestPath, written))
        {
            diagnostics.Add(
                BakeSeverity.Warning,
                BakeDiagnosticCodes.IncrementalManifestWriteFailed,
                $"The bake succeeded, but the incremental manifest '{manifestPath}' could not be "
                    + "written; the next bake runs in full.",
                manifestPath);
            return WithUpdatedDiagnostics(result, diagnostics);
        }

        return result;
    }

    private static bool IsUpToDate(
        BakeIncrementalManifest manifest,
        BakeIncrementalInputs inputs)
    {
        if (manifest.Version != BakeIncrementalManifest.CurrentVersion
            || !string.Equals(manifest.ToolVersion, BakeToolInfo.Version, StringComparison.Ordinal)
            || !string.Equals(
                manifest.ConfigurationHash,
                inputs.ConfigurationHash,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.ShapeClosureHash,
                inputs.ShapeClosureHash,
                StringComparison.Ordinal)
            || !string.Equals(manifest.Mode, inputs.Mode, StringComparison.Ordinal)
            || manifest.Inputs.Count != inputs.Inputs.Count
            || manifest.Outputs.Count != inputs.Outputs.Count)
        {
            return false;
        }

        for (int index = 0; index < inputs.Inputs.Count; index++)
        {
            if (!string.Equals(
                    manifest.Inputs[index].Path,
                    inputs.Inputs[index].Path,
                    StringComparison.Ordinal)
                || !string.Equals(
                    manifest.Inputs[index].Hash,
                    inputs.Inputs[index].Hash,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (int index = 0; index < inputs.Outputs.Count; index++)
        {
            if (!string.Equals(
                    manifest.Outputs[index].Path,
                    inputs.Outputs[index],
                    StringComparison.Ordinal)
                || !OutputMatches(manifest.Outputs[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OutputMatches(BakeOutputHash output)
    {
        try
        {
            return File.Exists(output.Path)
                && string.Equals(
                    BakeHash.HashFile(output.Path),
                    output.Hash,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateManifest(
        BakeIncrementalInputs inputs,
        BakeResult result,
        [NotNullWhen(true)] out BakeIncrementalManifest? manifest)
    {
        manifest = null;

        var outputs = new List<BakeOutputHash>(result.Packs.Count);
        foreach (BakedPackResult pack in result.Packs)
        {
            string hash;
            try
            {
                hash = BakeHash.HashFile(pack.PackPath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                return false;
            }

            outputs.Add(new BakeOutputHash(pack.PackPath, hash));
        }

        manifest = new BakeIncrementalManifest(
            BakeToolInfo.Version,
            inputs.ConfigurationHash,
            inputs.ShapeClosureHash,
            inputs.Mode,
            inputs.Inputs,
            outputs,
            result.AssetCount,
            result.TextureCount,
            result.ReferenceCount);

        return true;
    }

    private static BakeResult UpToDateResult(
        BakeProject project,
        BakeIncrementalManifest manifest,
        BakeDiagnostics diagnostics)
    {
        var packs = new List<BakedPackResult>(project.Packs.Count);
        for (int index = 0; index < project.Packs.Count; index++)
        {
            BakePack pack = project.Packs[index];
            packs.Add(new BakedPackResult(
                pack,
                manifest.Outputs[index].Path,
                packWritten: false,
                assetCount: pack.IsMain ? manifest.AssetCount : 0,
                textureCount: pack.IsMain ? manifest.TextureCount : 0,
                referenceCount: pack.IsMain ? manifest.ReferenceCount : 0,
                entries: []));
        }

        return new BakeResult(
            BakeStatus.UpToDate,
            BakeFailureStage.None,
            packs[0].PackPath,
            packWritten: false,
            manifest.AssetCount,
            manifest.TextureCount,
            manifest.ReferenceCount,
            packs,
            entries: [],
            diagnostics.GetSorted());
    }

    private static BakeResult WithUpdatedDiagnostics(
        BakeResult result,
        BakeDiagnostics diagnostics)
        => new(
            result.Status,
            result.FailureStage,
            result.PackPath,
            result.PackWritten,
            result.AssetCount,
            result.TextureCount,
            result.ReferenceCount,
            result.Packs,
            result.Entries,
            diagnostics.GetSorted());
}
