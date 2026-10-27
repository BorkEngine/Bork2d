using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Toml;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Discovers custom-data <c>.toml</c> sources under every configured pack root and reads each
/// source's own <c>[bork2d].type</c> declaration (D-M4-13, D-038).
/// </summary>
/// <remarks>
/// <para>
/// Discovery walks every pack root in builder order and, within a root, the whole tree in ordinal
/// path order (skipping reparse points so a link cannot leave the root), and uses
/// <see cref="BakeSourcePath"/> as the only path grammar. There is no prefix mapping: a data source
/// loses its <c>.toml</c> suffix and its canonical asset path is the source path relative to that
/// pack's root, wherever it lives.
/// </para>
/// <para>
/// The declared <c>[bork2d].type</c> is resolved against the project's explicit
/// <c>RegisterAsset&lt;T&gt;()</c> dictionary. An unregistered full name is a <c>BK1xxx</c> error at
/// the <c>type</c> value with a <c>RegisterAsset&lt;T&gt;()</c> hint, and the source is skipped.
/// </para>
/// <para>
/// A <c>.toml</c> whose name is <c>&lt;image&gt;.toml</c> and that has no root <c>bork2d</c> table is a
/// texture sidecar and is left to the texture pipeline; a typed <c>.toml</c> with that shape is data.
/// A type-less <c>.toml</c> that is not sidecar-shaped is a missing-type error. Two data sources in
/// the same pack that canonicalize to the same asset path are a duplicate error; the same canonical
/// path in different packs is allowed (the runtime overlays packs).
/// </para>
/// </remarks>
internal static class BakeSourceDiscovery
{
    private static readonly HashSet<string> _imageExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".tga"],
        StringComparer.OrdinalIgnoreCase);

    private const string TomlSuffix = ".toml";

    /// <summary>Discovers the custom-data sources of every configured pack.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>
    /// The discovered sources, ordered by pack index and then by canonical asset path.
    /// </returns>
    internal static IReadOnlyList<BakeSource> Discover(
        BakeProject project,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var sources = new List<BakeSource>();
        foreach (BakePack pack in project.Packs)
        {
            sources.AddRange(DiscoverPack(project, pack, diagnostics));
        }

        return sources;
    }

    /// <summary>Discovers the custom-data sources of one pack root.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="pack">The pack whose root is scanned.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The pack's discovered sources, sorted ordinal by canonical asset path.</returns>
    internal static IReadOnlyList<BakeSource> DiscoverPack(
        BakeProject project,
        BakePack pack,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var byAssetPath = new Dictionary<string, BakeSource>(StringComparer.Ordinal);

        foreach (string file in BakeSourcePath.EnumerateFilesSorted(pack.Root))
        {
            if (!file.EndsWith(TomlSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string diagnosticPath = BakeDiagnosticPath.ForSource(project.ProjectRoot, file);

            if (!BakeSourcePath.TryMakeRelative(
                pack.Root,
                file,
                out string relativePath,
                out string? relativeReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.SourceReadFailed,
                    $"The source '{file}' in pack '{pack.PackPath}' is not usable: {relativeReason}.",
                    diagnosticPath);
                continue;
            }

            if (IsSidecarShaped(relativePath))
            {
                // The texture pipeline owns a sidecar-shaped document unless it declares a root
                // [bork2d] table. A typed document with this name is data and is diagnosed here (its
                // asset path contains a dot); a text or syntax failure is left to the sidecar parser
                // so the same file is not reported twice.
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
                if (!IsSidecarShaped(relativePath))
                {
                    diagnostics.Add(
                        BakeSeverity.Error,
                        BakeDiagnosticCodes.SourceTypeMissing,
                        $"The source '{relativePath}' in pack '{pack.PackPath}' has no root "
                            + "'[bork2d]' table; every custom-data TOML must declare its full CLR "
                            + "type name as [bork2d] type = \"Full.Type.Name\".",
                        diagnosticPath);
                }

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

            if (byAssetPath.TryGetValue(assetPath, out BakeSource? existing))
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
}
