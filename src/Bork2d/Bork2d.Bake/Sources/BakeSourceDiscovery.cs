using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Discovers custom-data <c>.toml</c> sources under the asset root and maps each to its type.
/// </summary>
/// <remarks>
/// <para>
/// Discovery walks the asset root in ordinal path order, skips reparse points (so a link cannot leave
/// the root), and uses <see cref="BakeSourcePath"/> as the only path grammar. A data source loses its
/// <c>.toml</c> suffix; a file under <c>textures/</c> is a WP3 texture sidecar and is ignored by WP2.
/// A <c>.toml</c> file outside a mapped prefix is a <see cref="BakeDiagnosticCodes.SourceUnmapped"/>
/// error (Q-13), and two sources that canonicalize to the same asset path are a
/// <see cref="BakeDiagnosticCodes.SourceDuplicate"/> error.
/// </para>
/// <para>
/// The longest prefix on a <c>/</c> segment boundary wins; the descriptor parser already guarantees
/// unique exact prefixes. Non-TOML files are ignored.
/// </para>
/// </remarks>
internal static class BakeSourceDiscovery
{
    private const string TextureArea = "textures";

    /// <summary>Discovers the custom-data sources of a project.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The discovered sources, sorted ordinal by canonical asset path.</returns>
    internal static IReadOnlyList<BakeSource> Discover(
        ProjectDescriptor descriptor,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var byAssetPath = new Dictionary<string, BakeSource>(StringComparer.Ordinal);

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(descriptor.AssetsRoot))
        {
            if (!file.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string diagnosticPath = BakeDiagnosticPath.ForSource(
                descriptor.DescriptorDirectory,
                file);

            if (!BakeSourcePath.TryMakeRelative(
                descriptor.AssetsRoot,
                file,
                out string relativePath,
                out string? relativeReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceReadFailed,
                    $"The source '{file}' is not usable: {relativeReason}.",
                    diagnosticPath);
                continue;
            }

            if (IsTextureSidecar(relativePath))
            {
                // Texture sidecars belong to WP3; WP2 never reads them, and a sidecar name keeps
                // the source image's extension, so it must be ignored before asset-path validation.
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
                    BakeDiagnosticCodes.SourceUnmapped,
                    $"The source '{relativePath}' does not map to a canonical asset path: "
                        + $"{pathReason}.",
                    diagnosticPath);
                continue;
            }


            ProjectTypeMapping? mapping = FindMapping(descriptor.Types, assetPath);
            if (mapping is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceUnmapped,
                    $"The source '{relativePath}' (asset '{assetPath}') is not covered by any "
                        + "[[types]] prefix in the project descriptor.",
                    diagnosticPath,
                    assetPath: assetPath);
                continue;
            }

            var source = new BakeSource(
                relativePath,
                file,
                assetPath,
                mapping.TypeName,
                mapping.Prefix);

            if (byAssetPath.TryGetValue(assetPath, out BakeSource? existing))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceDuplicate,
                    $"The sources '{existing.RelativePath}' and '{relativePath}' both map to the "
                        + $"canonical asset path '{assetPath}'.",
                    diagnosticPath,
                    assetPath: assetPath);
                continue;
            }

            byAssetPath.Add(assetPath, source);
        }

        return [.. byAssetPath.Values.OrderBy(
            source => source.AssetPath,
            StringComparer.Ordinal)];
    }

    /// <summary>Finds the longest mapping prefix that covers an asset path.</summary>
    /// <param name="mappings">The descriptor mappings.</param>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <returns>The winning mapping, or <see langword="null"/> when none matches.</returns>
    internal static ProjectTypeMapping? FindMapping(
        IReadOnlyList<ProjectTypeMapping> mappings,
        string assetPath)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(assetPath);

        ProjectTypeMapping? best = null;
        foreach (ProjectTypeMapping mapping in mappings)
        {
            if (!MatchesPrefix(mapping.Prefix, assetPath))
            {
                continue;
            }

            if (best is null || mapping.Prefix.Length > best.Prefix.Length)
            {
                best = mapping;
            }
        }

        return best;
    }

    private static bool MatchesPrefix(string prefix, string assetPath)
    {
        if (string.Equals(assetPath, prefix, StringComparison.Ordinal))
        {
            return true;
        }

        return assetPath.Length > prefix.Length
            && assetPath[prefix.Length] == '/'
            && assetPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool IsTextureSidecar(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(TextureArea + "/", StringComparison.OrdinalIgnoreCase);
    }
}
