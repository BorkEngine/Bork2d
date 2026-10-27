using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Processors;
using Bork2d.Bake.Toml;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Discovers and classifies every source under every configured pack root: custom-data TOML,
/// processor-owned non-TOML sources, and processor sidecars (D-036/D-038).
/// </summary>
/// <remarks>
/// <para>
/// Discovery walks every pack root in builder order and, within a root, the whole tree in ordinal
/// path order (skipping reparse points so a link cannot leave the root). Non-TOML files are matched
/// against the project's immutable processor registry by their longest registered compound suffix;
/// everything else is ignored. <see cref="BakeSourcePath"/> remains the only path grammar.
/// </para>
/// <para>
/// A <c>.toml</c> whose exact pack-relative path is <c>&lt;source relative path&gt;.toml</c> for a
/// registered <see cref="BakeSidecarPolicy.Required"/> processor source is that source's
/// sidecar. Filename and registration decide classification; the file is never read for
/// classification, so a <c>[bork2d]</c> table inside it is an unknown sidecar key at binding time.
/// Every other <c>.toml</c> follows the data rules: a typed document is a custom-data source, and an
/// untyped document is a missing-type error unless it is an orphan sidecar, an ordinary TOML next to
/// a <see cref="BakeSidecarPolicy.None"/> processor, or the legacy image sidecar left to
/// the texture pipeline (removed in WP3).
/// </para>
/// <para>
/// The returned data and processor sources are each ordered by pack index and then canonical path.
/// </para>
/// </remarks>
internal static class BakeSourceDiscovery
{
    private static readonly HashSet<string> _imageExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"],
        StringComparer.OrdinalIgnoreCase);

    private const string TomlSuffix = ".toml";

    /// <summary>Discovers and classifies the sources of every configured pack.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The data and processor sources, ordered deterministically.</returns>
    internal static BakeDiscoveredSources Discover(
        BakeProject project,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var dataSources = new List<BakeSource>();
        var processorSources = new List<BakeProcessorSource>();

        foreach (BakePack pack in project.Packs)
        {
            DiscoverPack(project, pack, diagnostics, dataSources, processorSources);
        }

        dataSources.Sort(static (left, right) =>
        {
            int result = left.Pack.Index.CompareTo(right.Pack.Index);
            return result != 0 ? result : string.CompareOrdinal(left.AssetPath, right.AssetPath);
        });
        processorSources.Sort(static (left, right) =>
        {
            int result = left.Pack.Index.CompareTo(right.Pack.Index);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(left.BaseAssetPath, right.BaseAssetPath);
            return result != 0 ? result : string.CompareOrdinal(left.RelativePath, right.RelativePath);
        });

        return new BakeDiscoveredSources(dataSources, processorSources);
    }

    /// <summary>Tests whether a <c>.toml</c> name has the legacy texture-sidecar shape.</summary>
    /// <param name="relativePath">The pack-root-relative TOML path.</param>
    /// <returns><see langword="true"/> when it is <c>&lt;image extension&gt;.toml</c>.</returns>
    internal static bool IsSidecarShaped(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (!relativePath.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string sourceName = relativePath[..^TomlSuffix.Length];
        return _imageExtensions.Contains(Path.GetExtension(sourceName));
    }

    private static void DiscoverPack(
        BakeProject project,
        BakePack pack,
        BakeDiagnostics diagnostics,
        List<BakeSource> dataSources,
        List<BakeProcessorSource> processorSources)
    {
        var tomlFiles = new List<(string File, string RelativePath)>();
        var requiredByRelativePath = new Dictionary<string, BakeProcessorSource>(
            StringComparer.OrdinalIgnoreCase);
        var matchedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(pack.Root))
        {
            if (!BakeSourcePath.TryMakeRelative(
                    pack.Root,
                    file,
                    out string relativePath,
                    out string? relativeReason))
            {
                if (file.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.SourceReadFailed,
                        $"The source '{file}' in pack '{pack.PackPath}' is not usable: "
                            + $"{relativeReason}.",
                        BakeDiagnosticPath.ForSource(project.ProjectRoot, file));
                }

                continue;
            }

            if (file.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase))
            {
                tomlFiles.Add((file, relativePath));
                continue;
            }

            if (!project.ProcessorRegistry.TryMatch(relativePath, out BakeProcessorMatch match))
            {
                continue;
            }

            matchedRelativePaths.Add(relativePath);

            if (!BakeSourcePath.TryToAssetPath(
                    relativePath,
                    match.Extension,
                    out string baseAssetPath,
                    out string? pathReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.ProcessorInputInvalid,
                    $"The processor source '{relativePath}' in pack '{pack.PackPath}' does not map "
                        + $"to a canonical base asset path: {pathReason}. The processor "
                        + $"'{match.Processor.Name}' owns the '{match.Extension}' extension.",
                    BakeDiagnosticPath.ForSource(project.ProjectRoot, file));
                continue;
            }

            var source = new BakeProcessorSource(
                pack,
                relativePath,
                file,
                baseAssetPath,
                match.Extension,
                match.Processor);
            processorSources.Add(source);

            if (match.Processor.Policy == BakeSidecarPolicy.Required)
            {
                requiredByRelativePath[relativePath] = source;
            }
        }

        tomlFiles.Sort(static (left, right) =>
            string.CompareOrdinal(left.RelativePath, right.RelativePath));

        var dataByAssetPath = new Dictionary<string, BakeSource>(StringComparer.Ordinal);

        foreach ((string file, string relativePath) in tomlFiles)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(project.ProjectRoot, file);
            string stem = relativePath[..^TomlSuffix.Length];

            if (requiredByRelativePath.TryGetValue(stem, out BakeProcessorSource? sidecarOwner))
            {
                AttachSidecar(sidecarOwner, file, relativePath, diagnosticPath, diagnostics);
                continue;
            }

            if (matchedRelativePaths.Contains(stem)
                && project.ProcessorRegistry.TryMatch(stem, out BakeProcessorMatch invalidOwner)
                && invalidOwner.Processor.Policy == BakeSidecarPolicy.Required)
            {
                // The source exists but its own BK7002 already reports the invalid base path; the
                // sidecar belongs to it and must not add a second diagnostic.
                continue;
            }

            if (IsLegacyImageSidecar(project, relativePath, stem))
            {
                // The texture pipeline owns a legacy sidecar-shaped document unless it declares a
                // root [bork2d] table. Leave a read or syntax failure to that pipeline so the same
                // file is not reported twice. WP3 removes this bridge.
                string? candidate = TryReadSilently(file);
                if (candidate is null || !BakeSourceMetadataReader.TryHasBork2dTable(candidate))
                {
                    continue;
                }
            }

            if (!BakeSourceText.TryRead(
                    file,
                    relativePath,
                    diagnosticPath,
                    assetPath: null,
                    diagnostics,
                    out string text))
            {
                continue;
            }

            BakeSourceMetadata metadata = BakeSourceMetadataReader.Read(
                text,
                diagnosticPath,
                diagnostics);

            if (metadata.Kind == BakeSourceMetadataKind.Untyped)
            {
                ReportUntypedToml(
                    project,
                    pack,
                    relativePath,
                    stem,
                    matchedRelativePaths,
                    diagnosticPath,
                    diagnostics);
                continue;
            }

            if (metadata.Kind != BakeSourceMetadataKind.Typed)
            {
                continue;
            }

            if (!project.AssetTypes.ContainsKey(metadata.TypeName!))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.AssetTypeNotRegistered,
                    $"The source '{relativePath}' declares [bork2d] type = \"{metadata.TypeName}\", "
                        + "which is not registered. Register the data type in the assets executable "
                        + $"with RegisterAsset<{metadata.TypeName}>().",
                    diagnosticPath,
                    metadata.TypeLine,
                    metadata.TypeColumn,
                    assetPath: null);
                continue;
            }

            if (!BakeSourcePath.TryToAssetPath(
                relativePath,
                BakeSourceKind.DataToml,
                out string assetPath,
                out string? pathReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceInvalidAssetPath,
                    $"The source '{relativePath}' in pack '{pack.PackPath}' does not map to a "
                        + $"canonical asset path: {pathReason}. A data TOML name must not contain an "
                        + "extension or dot.",
                    diagnosticPath,
                    metadata.TypeLine,
                    metadata.TypeColumn);
                continue;
            }

            var source = new BakeSource(
                pack,
                relativePath,
                file,
                assetPath,
                metadata.TypeName!,
                metadata.TypeLine,
                metadata.TypeColumn);

            if (dataByAssetPath.TryGetValue(assetPath, out BakeSource? existing))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceDuplicate,
                    $"The sources '{existing.RelativePath}' and '{relativePath}' in pack "
                        + $"'{pack.PackPath}' both map to the canonical asset path '{assetPath}'.",
                    diagnosticPath,
                    assetPath: assetPath);
                continue;
            }

            dataByAssetPath.Add(assetPath, source);
            dataSources.Add(source);
        }
    }

    private static void AttachSidecar(
        BakeProcessorSource source,
        string file,
        string relativePath,
        string diagnosticPath,
        BakeDiagnostics diagnostics)
    {
        if (source.SidecarPath is not null)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SidecarDuplicate,
                $"The sidecar '{relativePath}' and '{source.SidecarRelativePath}' both target the "
                    + $"processor source '{source.RelativePath}'; a source may have at most one "
                    + "sidecar.",
                diagnosticPath);
            return;
        }

        source.SidecarPath = file;
        source.SidecarRelativePath = relativePath;
    }

    private static bool IsLegacyImageSidecar(
        BakeProject project,
        string relativePath,
        string stem)
        => IsSidecarShaped(relativePath)
            && !project.ProcessorRegistry.TryMatch(stem, out _);

    private static void ReportUntypedToml(
        BakeProject project,
        BakePack pack,
        string relativePath,
        string stem,
        HashSet<string> matchedRelativePaths,
        string diagnosticPath,
        BakeDiagnostics diagnostics)
    {
        if (project.ProcessorRegistry.TryMatch(stem, out BakeProcessorMatch match))
        {
            if (match.Processor.Policy == BakeSidecarPolicy.Required)
            {
                if (matchedRelativePaths.Contains(stem))
                {
                    // The source exists but its own BK7002 already reports why it cannot bake.
                    return;
                }

                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SidecarOrphaned,
                    $"The sidecar '{relativePath}' has no matching source file for the required-"
                        + $"sidecar processor '{match.Processor.Name}'.",
                    diagnosticPath);
                return;
            }
        }

        diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.SourceTypeMissing,
            $"The source '{relativePath}' in pack '{pack.PackPath}' has no root "
                + "'[bork2d]' table; every custom-data TOML must declare its full CLR "
                + "type name as [bork2d] type = \"Full.Type.Name\".",
            diagnosticPath);
    }

    private static string? TryReadSilently(string fullPath)
    {
        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
