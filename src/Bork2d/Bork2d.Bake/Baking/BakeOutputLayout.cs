using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Baking;

/// <summary>
/// One resolved pack output: its configured pack and the absolute path it is written to.
/// </summary>
/// <param name="Pack">The configured pack.</param>
/// <param name="Path">The absolute pack output path under the output root.</param>
internal readonly record struct BakePackOutput(BakePack Pack, string Path);

/// <summary>
/// Resolves every configured pack path against an output root.
/// </summary>
/// <remarks>
/// <para>
/// The output root is an invocation input: the build target passes <c>$(TargetDir)</c> and the
/// command handler defaults to <see cref="AppContext.BaseDirectory"/>; <c>--output-root</c> overrides
/// it. It may be outside the project root because MSBuild writes into the app's output directory.
/// Pack paths are configuration-relative paths, so they keep the same relative layout under any
/// output root and may contain subdirectories.
/// </para>
/// <para>
/// <see cref="BakeProjectFactory"/> already rejected rooted paths and empty/<c>.</c>/<c>..</c>
/// segments; this type joins the paths and reaffirms containment so a resolved output can never escape
/// the root.
/// </para>
/// </remarks>
internal static class BakeOutputLayout
{
    /// <summary>Resolves the output root and every pack output path.</summary>
    /// <param name="project">The validated project configuration.</param>
    /// <param name="outputRoot">The requested absolute or current-directory-relative output root.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="root">The absolute output root.</param>
    /// <param name="outputs">The resolved outputs in pack order.</param>
    /// <returns><see langword="true"/> when every output path resolved inside the output root.</returns>
    internal static bool TryResolve(
        BakeProject project,
        string outputRoot,
        BakeDiagnostics diagnostics,
        out string root,
        out IReadOnlyList<BakePackOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(diagnostics);

        root = string.Empty;
        outputs = [];

        try
        {
            root = Path.GetFullPath(outputRoot);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.OutputRootInvalid,
                $"The output root '{outputRoot}' is not a valid path: {exception.Message}",
                outputRoot);
            return false;
        }

        var resolved = new List<BakePackOutput>(project.Packs.Count);
        foreach (BakePack pack in project.Packs)
        {
            string path;
            try
            {
                path = Path.GetFullPath(
                    pack.PackPath.Replace('/', Path.DirectorySeparatorChar),
                    root);
            }
            catch (Exception exception) when (IsPathFailure(exception))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.OutputRootInvalid,
                    $"The pack path '{pack.PackPath}' cannot be resolved under the output root "
                        + $"'{root}': {exception.Message}",
                    pack.PackPath);
                return false;
            }

            if (!IsUnder(root, path))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.OutputRootInvalid,
                    $"The pack path '{pack.PackPath}' resolves outside the output root '{root}'.",
                    pack.PackPath);
                return false;
            }

            resolved.Add(new BakePackOutput(pack, path));
        }

        outputs = resolved;
        return true;
    }

    private static bool IsUnder(string root, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        if (string.Equals(normalizedRoot, Path.TrimEndingDirectorySeparator(path), comparison))
        {
            return false;
        }

        return path.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;
}
