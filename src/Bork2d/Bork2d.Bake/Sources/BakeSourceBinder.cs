using Bork2d.Bake.Baking;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Toml;
using Tomlyn.Model;

namespace Bork2d.Bake.Sources;

/// <summary>
/// Reads every discovered source, parses it, and binds it through the project's explicit data-type
/// registrations (D-038).
/// </summary>
/// <remarks>
/// <para>
/// Binding is sequential and ordinal by canonical asset path, so diagnostics are deterministic. The
/// caller discovers the sources first (D-M4-13): discovery also reads and resolves each source's
/// <c>[bork2d].type</c>, so the binder never has to guess a mapping. A source that fails to read,
/// parse, or bind is dropped from the result while its diagnostics stay in the bag.
/// </para>
/// <para>
/// Source text is strict UTF-8 with an optional BOM (shared with discovery). This pass does not
/// validate references against the baked set and never writes a pack.
/// </para>
/// </remarks>
internal static class BakeSourceBinder
{
    /// <summary>Binds every discovered custom-data source of a project.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="sources">The discovered sources with their declared types.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns>The successfully bound assets, sorted ordinal by canonical asset path.</returns>
    internal static IReadOnlyList<BoundAsset> BindAll(
        BakeProject project,
        IReadOnlyList<BakeSource> sources,
        BakeDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var assets = new List<BoundAsset>(sources.Count);

        foreach (BakeSource source in sources)
        {
            string diagnosticPath = BakeDiagnosticPath.ForSource(
                project.ProjectRoot,
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

            if (!project.AssetTypes.TryGetValue(source.TypeName, out BakeAssetTypeRegistration? registration))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.AssetTypeNotRegistered,
                    $"The source '{source.RelativePath}' declares [bork2d] type = "
                        + $"\"{source.TypeName}\", which is not registered. Register the data type "
                        + "in the assets executable with RegisterAsset<T>().",
                    diagnosticPath,
                    source.TypeLine,
                    source.TypeColumn);
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
                value = registration.Bind(context);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.BinderUnexpected,
                    $"Binding '{source.AssetPath}' through '{source.TypeName}' failed unexpectedly: "
                        + exception.Message,
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
                    BakeDiagnosticCodes.BinderUnexpected,
                    $"Binding '{source.AssetPath}' through '{source.TypeName}' returned no value "
                        + "without a diagnostic.",
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
