using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Wraps the full bake pipeline with the incremental skip decision and the manifest record.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline stays a full bake (WP4); this planner decides whether that full bake can be skipped
/// (M4 §10.3, Q-09), runs it otherwise, and records a successful run in the manifest. A skip requires
/// all of: a manifest with the current schema version and tool version, the same descriptor hash, the
/// same tracked input list, the same game-side assembly closure, the same generated host assembly,
/// the same mode and output path, and an output pack that exists with the recorded hash.
/// </para>
/// <para>
/// <c>--check</c> never skips and never writes a manifest. <c>--force</c> always runs the full bake.
/// A missing, corrupt, or unreadable manifest, a missing pack, or an externally modified pack all
/// fall through to a full bake. A failed bake returns before the manifest write, so the previous pack
/// and manifest both stay unchanged.
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

        string packPath = options.OutputPath ?? descriptor.PackPath;
        BakeIncrementalInputs? current = BakeIncrementalInputs.TryCollect(
            descriptor,
            options.HostPath,
            options.Mode,
            packPath);

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

    /// <summary>Tests a manifest against the current inputs (the pure skip rule).</summary>
    /// <param name="manifest">The recorded manifest.</param>
    /// <param name="current">The recomputed inputs.</param>
    /// <param name="toolVersion">The current bake tool version.</param>
    /// <param name="packHash">The current output pack hash.</param>
    /// <returns><see langword="true"/> when the bake can be skipped.</returns>
    internal static bool Matches(
        BakeIncrementalManifest manifest,
        BakeIncrementalInputs current,
        string toolVersion,
        string packHash)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(toolVersion);
        ArgumentNullException.ThrowIfNull(packHash);

        if (manifest.Version != BakeIncrementalManifest.CurrentVersion
            || !string.Equals(manifest.ToolVersion, toolVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.DescriptorHash, current.DescriptorHash, StringComparison.Ordinal)
            || !string.Equals(
                manifest.CommonAssemblyHash,
                current.CommonAssemblyHash,
                StringComparison.Ordinal)
            || !string.Equals(manifest.HostHash, current.HostHash, StringComparison.Ordinal)
            || !string.Equals(manifest.Mode, current.Mode, StringComparison.Ordinal)
            || !string.Equals(manifest.OutputHash, packHash, StringComparison.Ordinal)
            || !PathEquals(manifest.OutputPath, current.OutputPath)
            || manifest.Inputs.Count != current.Inputs.Count)
        {
            return false;
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
        if (manifest is null || !File.Exists(current.OutputPath))
        {
            return null;
        }

        string packHash;
        try
        {
            packHash = BakeHash.HashFile(current.OutputPath);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return null;
        }

        if (!Matches(manifest, current, BakeToolInfo.Version, packHash))
        {
            return null;
        }

        return new BakeResult(
            BakeStatus.UpToDate,
            BakeFailureStage.None,
            current.OutputPath,
            packWritten: false,
            manifest.AssetCount,
            manifest.TextureCount,
            manifest.ReferenceCount,
            entries: [],
            diagnostics: []);
    }

    private static void RecordManifest(
        ProjectDescriptor descriptor,
        BakeIncrementalInputs current,
        BakeResult result,
        BakeDiagnostics diagnostics)
    {
        if (result.PackPath is null)
        {
            return;
        }

        string packPath;
        string packHash;
        try
        {
            packPath = Path.GetFullPath(result.PackPath);
            packHash = BakeHash.HashFile(packPath);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Warning,
                BakeDiagnosticCodes.IncrementalManifestWriteFailed,
                $"The incremental manifest could not record the pack '{result.PackPath}': "
                    + $"{exception.Message}; the next bake runs fully.",
                result.PackPath);
            return;
        }

        var manifest = new BakeIncrementalManifest(
            BakeIncrementalManifest.CurrentVersion,
            BakeToolInfo.Version,
            current.DescriptorHash,
            current.Inputs,
            current.CommonAssemblyHash,
            current.HostHash,
            current.Mode,
            packPath,
            packHash,
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
