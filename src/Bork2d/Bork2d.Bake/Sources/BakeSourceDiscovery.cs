using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Discovers custom-data <c>.toml</c> sources under the asset root and reads each source's own
/// <c>[bork2d].type</c> declaration (D-M4-13).
/// </summary>
/// <remarks>
/// <para>
/// Discovery walks the whole asset root in ordinal path order (skipping reparse points so a link
/// cannot leave the root), excludes the descriptor itself, and uses <see cref="BakeSourcePath"/> as
/// the only path grammar. There is no prefix mapping: a data source loses its <c>.toml</c> suffix and
/// its canonical asset path is the source path relative to the asset root, wherever it lives.
/// </para>
/// <para>
/// A <c>.toml</c> whose name is <c>&lt;image&gt;.toml</c> and that has no root <c>bork2d</c> table is a
/// texture sidecar and is left to the texture pipeline; a typed <c>.toml</c> with that shape is data.
/// A type-less <c>.toml</c> that is not sidecar-shaped is a missing-type error. Two data sources that
/// canonicalize to the same asset path are a duplicate error.
/// </para>
/// </remarks>
internal static class BakeSourceDiscovery
{
    private static readonly HashSet<string> _imageExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"],
        StringComparer.OrdinalIgnoreCase);

    private const string TomlSuffix = ".toml";

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
        string descriptorPath = Path.GetFullPath(descriptor.DescriptorPath);

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(descriptor.AssetsRoot))
        {
            if (!file.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase)
                || PathEquals(file, descriptorPath))
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

            bool sidecarShaped = IsSidecarShaped(relativePath);
            if (sidecarShaped)
            {
                // The texture pipeline owns a sidecar-shaped document unless it declares a root
                // [bork2d] table. A typed document with this name is data and is diagnosed here
                // (its asset path contains a dot); a text or syntax failure is left to the sidecar
                // parser so the same file is not reported twice.
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
                if (!sidecarShaped)
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.SourceTypeMissing,
                        $"The source '{relativePath}' has no root '[bork2d]' table; every "
                            + "custom-data TOML must declare its full CLR type name as "
                            + "[bork2d] type = \"Full.Type.Name\".",
                        diagnosticPath);
                }

                continue;
            }

            if (metadata.Kind != BakeSourceMetadataKind.Typed)
            {
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
                    $"The source '{relativePath}' does not map to a canonical asset path: "
                        + $"{pathReason}. A data TOML name must not contain an extension or dot.",
                    diagnosticPath,
                    metadata.TypeLine,
                    metadata.TypeColumn);
                continue;
            }

            var source = new BakeSource(
                relativePath,
                file,
                assetPath,
                metadata.TypeName!,
                metadata.TypeLine,
                metadata.TypeColumn);

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

    /// <summary>Tests whether a <c>.toml</c> name has the texture-sidecar shape.</summary>
    /// <param name="relativePath">The asset-root-relative TOML path.</param>
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

    private static bool PathEquals(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }
}
