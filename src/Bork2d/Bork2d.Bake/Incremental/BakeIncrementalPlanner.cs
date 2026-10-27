using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Wraps the full multi-pack bake pipeline with the incremental skip decision and the manifest record.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline stays a full bake; this planner decides whether it can be skipped (M4 §10.3, Q-09),
/// runs it otherwise, and records a successful run in the manifest. A skip requires all of: a manifest
/// with the current schema version and tool version, the same descriptor hash, the same tracked input
/// list across every pack root, the same game-side assembly closure, the same generated host assembly,
/// the same mode, the same ordered output paths, and every output pack present with its recorded hash.
/// </para>
/// <para>
/// The decision is all-or-nothing (D-M4-14): one missing or changed pack re-bakes every pack. There is
/// no per-pack cache in M4. <c>--check</c> never skips and never writes a manifest. <c>--force</c>
/// always runs the full bake. A missing, corrupt, or unreadable manifest, a missing pack, or an
/// externally modified pack all fall through to a full bake. A failed bake returns before the manifest
/// write, so the previous packs and manifest stay unchanged.
/// </para>
/// </remarks>
internal static class BakeIncrementalPlanner
{
    /// <summary>Runs a bake with the incremental skip decision.</summary>
    /// <param name="options">The resolved invocation inputs.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The result, which is either up to date or a full-bake outcome.</returns>
    internal static BakeResult Run(BakeOptions options, BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options.Check)
        {
            return BakePipeline.Run(options, diagnostics);
        }

        var descriptorDiagnostics = new BakeDiagnostics();
        ProjectDescriptor? descriptor = ProjectDescriptorParser.Parse(
            options.ProjectPath,
            descriptorDiagnostics);
        if (descriptor is null || descriptorDiagnostics.HasErrors)
        {
            // Let the pipeline parse again and report the canonical diagnostics.
            return BakePipeline.Run(options, diagnostics);
        }

        BakeIncrementalInputs? current = BakeIncrementalInputs.TryCollect(
            descriptor,
            options.HostPath,
            options.Mode,
            options.OutputRoot);

        if (!options.Force && current is not null)
        {
            BakeResult? upToDate = TrySkip(descriptor, current);
            if (upToDate is not null)
            {
                return upToDate;
            }
        }

        BakeResult result = BakePipeline.Run(options, diagnostics);
        if (result.Status == BakeStatus.Success
            && result.PackWritten
            && current is not null)
        {
            RecordManifest(descriptor, current, result, diagnostics);
        }

        return result;
    }

    /// <summary>Tests a manifest against the current inputs and output hashes (the pure skip rule).</summary>
    /// <param name="manifest">The recorded manifest.</param>
    /// <param name="current">The recomputed inputs.</param>
    /// <param name="toolVersion">The current bake tool version.</param>
    /// <param name="outputHashes">The current output pack paths and hashes, in descriptor order.</param>
    /// <returns><see langword="true"/> when the bake can be skipped.</returns>
    internal static bool Matches(
        BakeIncrementalManifest manifest,
        BakeIncrementalInputs current,
        string toolVersion,
        IReadOnlyList<BakeOutputHash> outputHashes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(toolVersion);
        ArgumentNullException.ThrowIfNull(outputHashes);

        if (manifest.Version != BakeIncrementalManifest.CurrentVersion
            || !string.Equals(manifest.ToolVersion, toolVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.DescriptorHash, current.DescriptorHash, StringComparison.Ordinal)
            || !string.Equals(
                manifest.CommonAssemblyHash,
                current.CommonAssemblyHash,
                StringComparison.Ordinal)
            || !string.Equals(manifest.HostHash, current.HostHash, StringComparison.Ordinal)
            || !string.Equals(manifest.Mode, current.Mode, StringComparison.Ordinal)
            || manifest.Outputs.Count != current.Outputs.Count
            || manifest.Outputs.Count != outputHashes.Count
            || manifest.Inputs.Count != current.Inputs.Count)
        {
            return false;
        }

        for (int i = 0; i < manifest.Outputs.Count; i++)
        {
            BakeOutputHash recorded = manifest.Outputs[i];
            BakeOutputHash actual = outputHashes[i];
            if (!PathEquals(recorded.Path, current.Outputs[i])
                || !string.Equals(recorded.Path, actual.Path, StringComparison.Ordinal)
                || !string.Equals(recorded.Hash, actual.Hash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (int i = 0; i < manifest.Inputs.Count; i++)
        {
            BakeInputHash recorded = manifest.Inputs[i];
            BakeInputHash actual = current.Inputs[i];
            if (!string.Equals(recorded.Path, actual.Path, StringComparison.Ordinal)
                || !string.Equals(recorded.Hash, actual.Hash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static BakeResult? TrySkip(ProjectDescriptor descriptor, BakeIncrementalInputs current)
    {
        BakeIncrementalManifest? manifest = BakeManifestStore.TryRead(
            BakeManifestPaths.ManifestPath(descriptor.DescriptorDirectory));
        if (manifest is null)
        {
            return null;
        }

        var outputHashes = new List<BakeOutputHash>(current.Outputs.Count);
        foreach (string outputPath in current.Outputs)
        {
            if (!File.Exists(outputPath))
            {
                return null;
            }

            try
            {
                outputHashes.Add(new BakeOutputHash(outputPath, BakeHash.HashFile(outputPath)));
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                return null;
            }
        }

        if (!Matches(manifest, current, BakeToolInfo.Version, outputHashes))
        {
            return null;
        }

        var packs = new List<BakedPackResult>(descriptor.Packs.Count);
        for (int i = 0; i < descriptor.Packs.Count; i++)
        {
            packs.Add(new BakedPackResult(
                descriptor.Packs[i],
                current.Outputs[i],
                packWritten: false,
                assetCount: 0,
                textureCount: 0,
                referenceCount: 0,
                entries: []));
        }

        return new BakeResult(
            BakeStatus.UpToDate,
            BakeFailureStage.None,
            current.Outputs[0],
            packWritten: false,
            manifest.AssetCount,
            manifest.TextureCount,
            manifest.ReferenceCount,
            packs,
            entries: [],
            diagnostics: []);
    }

    private static void RecordManifest(
        ProjectDescriptor descriptor,
        BakeIncrementalInputs current,
        BakeResult result,
        BakeDiagnostics diagnostics)
    {
        var outputs = new List<BakeOutputHash>(result.Packs.Count);
        foreach (BakedPackResult pack in result.Packs)
        {
            try
            {
                string packPath = Path.GetFullPath(pack.PackPath);
                outputs.Add(new BakeOutputHash(packPath, BakeHash.HashFile(packPath)));
            }
            catch (Exception exception) when (IsIoFailure(exception))
            {
                diagnostics.Add(
                    BakeSeverity.Warning,
                    BakeDiagnosticCodes.IncrementalManifestWriteFailed,
                    $"The incremental manifest could not record the pack '{pack.PackPath}': "
                        + $"{exception.Message}; the next bake runs fully.",
                    pack.PackPath);
                return;
            }
        }

        var manifest = new BakeIncrementalManifest(
            BakeIncrementalManifest.CurrentVersion,
            BakeToolInfo.Version,
            current.DescriptorHash,
            current.Inputs,
            current.CommonAssemblyHash,
            current.HostHash,
            current.Mode,
            outputs,
            result.AssetCount,
            result.TextureCount,
            result.ReferenceCount);

        BakeManifestStore.TryWrite(
            BakeManifestPaths.ManifestPath(descriptor.DescriptorDirectory),
            manifest,
            diagnostics);
    }

    private static bool PathEquals(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static bool IsIoFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
}
