using System.Diagnostics.CodeAnalysis;
using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Paths;
using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Validates a <see cref="BakeProjectDefinition"/> into the immutable <see cref="BakeProject"/> the
/// pipeline consumes (D-036/D-038).
/// </summary>
/// <remarks>
/// <para>
/// All configuration is validated before any source is read. The project root comes from the command
/// line override, then the configured root, then the process current directory; it must exist. At
/// least one pack is required. Roots and pack paths are relative, canonical-safe, and cannot escape
/// the project root; roots must exist as directories. Pack paths stay relative because the output
/// root is a build/invocation input (normally <c>$(TargetDir)</c>) that may be outside the project
/// root; <see cref="BakeOutputLayout"/> resolves them under it later.
/// </para>
/// <para>
/// Semantic failures are <c>BK1xxx</c> diagnostics, never exceptions. The builder records its own
/// registration issues in the definition; the factory copies them into the diagnostics bag first, so
/// one run reports every configuration problem it can.
/// </para>
/// </remarks>
internal static class BakeProjectFactory
{
    /// <summary>Validates a definition and creates the immutable project.</summary>
    /// <param name="definition">The builder-side configuration.</param>
    /// <param name="projectRootOverride">
    /// The command-line project root override, or <see langword="null"/> to use the configured root or
    /// the process current directory.
    /// </param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="project">The created project, even when a configuration error was reported.</param>
    /// <returns><see langword="true"/> when the configuration is valid.</returns>
    internal static bool TryCreate(
        BakeProjectDefinition definition,
        string? projectRootOverride,
        BakeDiagnostics diagnostics,
        [NotNullWhen(true)] out BakeProject? project)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(diagnostics);

        project = null;

        foreach (BakeDiagnostic issue in definition.Issues)
        {
            diagnostics.Add(issue);
        }

        bool registryValid = BakeProcessorRegistry.TryCreate(
            definition.Processors,
            diagnostics,
            out BakeProcessorRegistry? processorRegistry);

        if (!TryResolveProjectRoot(definition, projectRootOverride, diagnostics, out string projectRoot))
        {
            return false;
        }

        if (definition.Packs.Count == 0)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PackRequired,
                "The bake configuration declares no asset pack; call WithPack(packPath, root) at "
                    + "least once. The first pack is the main pack.");
            return false;
        }

        var packs = new List<BakePack>(definition.Packs.Count);
        var packPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool valid = true;

        for (int index = 0; index < definition.Packs.Count; index++)
        {
            BakePackDefinition configured = definition.Packs[index];
            string label = configured.PackPath;

            if (!TryNormalizeRelativePath(configured.PackPath, out string packPath, out string? packReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackPathInvalid,
                    $"The pack path '{configured.PackPath}' is invalid: {packReason}. A pack path must be "
                        + "relative to the output root and must not contain an empty, '.' or '..' "
                        + "segment.",
                    ProjectFilePath(projectRoot, configured.PackPath));
                valid = false;
                continue;
            }

            if (!packPaths.Add(packPath))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackPathDuplicate,
                    $"The pack path '{packPath}' is configured more than once; every pack needs a "
                        + "unique output path.",
                    ProjectFilePath(projectRoot, packPath));
                valid = false;
                continue;
            }

            if (!TryNormalizeRelativePath(configured.Root, out string relativeRoot, out string? rootReason))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackRootInvalid,
                    $"The asset root '{configured.Root}' of pack '{label}' is invalid: {rootReason}. A "
                        + "root must be relative to the project root and must not contain an empty, "
                        + "'.' or '..' segment.",
                    ProjectFilePath(projectRoot, configured.Root));
                valid = false;
                continue;
            }

            string root;
            try
            {
                root = Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        relativeRoot.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (IsPathFailure(exception))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackRootInvalid,
                    $"The asset root '{configured.Root}' of pack '{label}' cannot be resolved under "
                        + $"the project root: {exception.Message}",
                    ProjectFilePath(projectRoot, relativeRoot));
                valid = false;
                continue;
            }

            if (!BakeSourcePath.IsUnderRoot(projectRoot, root)
                || string.Equals(
                    Path.TrimEndingDirectorySeparator(projectRoot),
                    Path.TrimEndingDirectorySeparator(root),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackRootInvalid,
                    $"The asset root '{configured.Root}' of pack '{label}' resolves outside the "
                        + $"project root '{projectRoot}'.",
                    ProjectFilePath(projectRoot, relativeRoot));
                valid = false;
                continue;
            }

            if (!roots.Add(root))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackRootDuplicate,
                    $"The asset root '{relativeRoot}' is configured by more than one pack; every pack "
                        + "needs a unique root.",
                    ProjectFilePath(projectRoot, relativeRoot));
                valid = false;
                continue;
            }

            if (!Directory.Exists(root))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PackRootMissing,
                    $"The asset root '{relativeRoot}' of pack '{label}' does not exist as a directory "
                        + $"('{root}').",
                    ProjectFilePath(projectRoot, relativeRoot));
                valid = false;
                continue;
            }

            packs.Add(new BakePack(index, root, relativeRoot, packPath));
        }

        if (packs.Count == 0)
        {
            // Every configured pack failed validation, so there is no valid main pack to build.
            return false;
        }

        var assetTypes = new Dictionary<string, BakeAssetTypeRegistration>(
            definition.AssetTypes,
            StringComparer.Ordinal);

        if (!registryValid || processorRegistry is null)
        {
            // An invalid processor registry is a configuration error: the project must not exist, so
            // no source can ever be read with a partially registered processor set.
            return false;
        }

        var projectValue = new BakeProject(
            projectRoot,
            packs,
            definition.TextureFilter,
            definition.TextureWrap,
            assetTypes,
            definition.Processors,
            processorRegistry,
            definition.UseDefaultProcessors);

        project = projectValue;
        return valid && !diagnostics.HasErrors;
    }

    private static bool TryResolveProjectRoot(
        BakeProjectDefinition definition,
        string? projectRootOverride,
        BakeDiagnostics diagnostics,
        out string projectRoot)
    {
        projectRoot = string.Empty;

        string requested = FirstNonEmpty(
            projectRootOverride,
            definition.ProjectRoot,
            Directory.GetCurrentDirectory());

        try
        {
            projectRoot = Path.GetFullPath(requested);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProjectRootInvalid,
                $"The project root '{requested}' is not a valid path: {exception.Message}");
            return false;
        }

        if (!Directory.Exists(projectRoot))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.ProjectRootMissing,
                $"The project root '{projectRoot}' does not exist as a directory. Pass "
                    + "--project-root or call WithProjectRoot().",
                projectRoot);
            return false;
        }

        return true;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool TryNormalizeRelativePath(
        string value,
        out string normalized,
        out string? reason)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "the path is empty";
            return false;
        }

        if (Path.IsPathRooted(value))
        {
            reason = "the path is rooted";
            return false;
        }

        string candidate = value.Replace('\\', '/');
        foreach (string segment in candidate.Split('/'))
        {
            if (segment.Length == 0)
            {
                reason = "the path contains an empty segment";
                return false;
            }

            if (segment is "." or "..")
            {
                reason = $"the path contains a '{segment}' segment";
                return false;
            }
        }

        normalized = candidate;
        reason = null;
        return true;
    }

    private static string ProjectFilePath(string projectRoot, string relativePath)
        => Path.GetFullPath(Path.Combine(
            projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;
}
