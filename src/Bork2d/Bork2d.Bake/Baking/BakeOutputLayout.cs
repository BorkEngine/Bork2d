using Bork2d.Bake.Diagnostics;
using Bork2d.Bake.Project;

namespace Bork2d.Bake.Baking;

/// <summary>
/// One resolved pack output: its descriptor entry and the absolute path it is written to.
/// </summary>
/// <param name="Pack">The descriptor's <c>[[assets]]</c> entry.</param>
/// <param name="Path">The absolute pack output path under the output root.</param>
internal readonly record struct BakePackOutput(ProjectPack Pack, string Path);

/// <summary>
/// Resolves every descriptor pack path against an output root (D-M4-14).
/// </summary>
/// <remarks>
/// <para>
/// The output root is the descriptor directory by default; <c>bake --output-root</c> and the MSBuild
/// target override it (normally with <c>$(TargetDir)</c>). Pack paths are descriptor-authored relative
/// paths, so they keep the same relative layout under any output root and may contain subdirectories.
/// The parser already rejected rooted paths and <c>.</c>/<c>..</c> segments; this type joins the paths
/// and reaffirms containment so a resolved output can never escape the root.
/// </para>
/// <para>
/// Resolution is deterministic and descriptor-ordered: index zero is the main pack and later entries
/// are DLC packs.
/// </para>
/// </remarks>
internal static class BakeOutputLayout
{
    /// <summary>Resolves the output root and every pack output path.</summary>
    /// <param name="descriptor">The parsed project descriptor.</param>
    /// <param name="outputRoot">The requested output root, or <see langword="null"/> for the descriptor directory.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <param name="root">The absolute output root.</param>
    /// <param name="outputs">The resolved outputs in descriptor order.</param>
    /// <returns><see langword="true"/> when every output path resolved inside the output root.</returns>
    internal static bool TryResolve(
        ProjectDescriptor descriptor,
        string? outputRoot,
        BakeDiagnostics diagnostics,
        out string root,
        out IReadOnlyList<BakePackOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(diagnostics);

        root = string.Empty;
        outputs = [];

        string requested = outputRoot ?? descriptor.DescriptorDirectory;
        try
        {
            root = Path.GetFullPath(requested);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.DescriptorInvalidValue,
                $"The output root '{requested}' is not a valid path: {exception.Message}",
                requested);
            return false;
        }

        var resolved = new List<BakePackOutput>(descriptor.Packs.Count);
        foreach (ProjectPack pack in descriptor.Packs)
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
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    $"The pack path '{pack.PackPath}' cannot be resolved under the output root "
                        + $"'{root}': {exception.Message}",
                    descriptor.DescriptorPath);
                return false;
            }

            if (!IsUnder(root, path))
            {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.DescriptorInvalidValue,
                    $"The pack path '{pack.PackPath}' resolves outside the output root '{root}'.",
                    descriptor.DescriptorPath);
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
