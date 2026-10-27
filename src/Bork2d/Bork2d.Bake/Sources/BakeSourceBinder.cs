using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Hosting;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Project;
using Bork2d.Bake.Toml;
using Tomlyn.Model;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Reads every discovered source, parses it, and binds it through the generated host manifest.
/// </summary>
/// <remarks>
/// <para>
/// Binding is sequential and ordinal by canonical asset path, so diagnostics are deterministic. The
/// caller discovers the sources first (D-M4-13): discovery also reads each source's
/// <c>[bork2d].type</c>, so the binder never has to guess a mapping and a source failure cannot reach
/// the host with an unknown type. A source that fails to read, parse, or bind is dropped from the
/// result while its diagnostics stay in the bag.
/// </para>
/// <para>
/// Source text is strict UTF-8 with an optional BOM (shared with discovery). This pass does not
/// validate references against the baked set and never writes a pack.
/// </para>
/// </remarks>
internal static class BakeSourceBinder
{
    /// <summary>Parses every discovered source for TOML syntax without binding it.</summary>
    /// <remarks>
    /// This is the <c>validate</c> command's host-free path. Discovery already parses the metadata of
    /// every data source; this method only re-reads the discovered files and proves their full TOML
    /// syntax parses. It cannot check types, required members, or references without the generated
    /// host.
    /// </remarks>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="sources">The discovered sources.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The number of sources whose TOML syntax parsed successfully.</returns>
    internal static int ValidateSyntax(
        ProjectDescriptor descriptor,
        IReadOnlyList<BakeSource> sources,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        int parsed = 0;
        foreach (BakeSource source in sources)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(
                descriptor.DescriptorDirectory,
                source.FullPath);

            if (!BakeSourceText.TryRead(
                    source.FullPath,
                    source.RelativePath,
                    diagnosticPath,
                    source.AssetPath,
                    diagnostics,
                    out string text))
            {
                continue;
            }

            if (TomlDocument.TryParse(text, diagnosticPath, diagnostics, out _, out _))
            {
                parsed++;
            }
        }

        return parsed;
    }

    /// <summary>Binds every discovered custom-data source of a project.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="sources">The discovered sources with their declared types.</param>
    /// <param name="manifest">The loaded generated host manifest.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The successfully bound assets, sorted ordinal by canonical asset path.</returns>
    internal static IReadOnlyList<BoundAsset> BindAll(
        ProjectDescriptor descriptor,
        IReadOnlyList<BakeSource> sources,
        IBakeManifest manifest,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var assets = new List<BoundAsset>(sources.Count);

        foreach (BakeSource source in sources)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(
                descriptor.DescriptorDirectory,
                source.FullPath);

            if (!BakeSourceText.TryRead(
                    source.FullPath,
                    source.RelativePath,
                    diagnosticPath,
                    source.AssetPath,
                    diagnostics,
                    out string text))
            {
                continue;
            }

            int before = diagnostics.Count;
            if (!TomlDocument.TryParse(
                text,
                diagnosticPath,
                diagnostics,
                out TomlTable root,
                out TomlSyntaxIndex syntax))
            {
                continue;
            }

            var context = new BakeBindContext(
                root,
                syntax,
                diagnostics,
                diagnosticPath,
                source.AssetPath);

            object? value;
            try
            {
                value = manifest.Bind(source.TypeName, context);
            }
            catch (BakeBindException exception)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostStale,
                    $"The bake host could not bind '{source.AssetPath}': {exception.Message}",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostLoadFailed,
                    $"Binding '{source.AssetPath}' failed: {exception.Message}",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }

            if (diagnostics.Count != before)
            {
                continue;
            }

            if (value is null)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.HostContractInvalid,
                    $"The bake host returned no value for '{source.AssetPath}' without a diagnostic.",
                    diagnosticPath,
                    assetPath: source.AssetPath);
                continue;
            }

            assets.Add(new BoundAsset(
                source.AssetPath,
                source.TypeName,
                value,
                diagnosticPath,
                source.RelativePath,
                context.References));
        }

        return assets;
    }
}
