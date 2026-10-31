using Bork2d.Engine.Assets.Vfs;

namespace Bork2d.Bake.Paths;

/// <summary>
/// Maps source-tree paths to canonical asset paths and validates traversal-safe containment under
/// the asset root.
/// </summary>
/// <remarks>
/// <para>
/// There is one path grammar: <see cref="AssetPath"/> is the canonical rules engine. This helper
/// only strips the matched source suffix before handing the result to it, so the baker cannot drift
/// from the runtime's lookup keys.
/// </para>
/// <para>
/// A data <c>.toml</c> source removes the <c>toml</c> extension; a processor source removes the
/// extension its processor registered. There is no per-format extension list here. Rooted paths,
/// empty paths, and <c>.</c>/<c>..</c> segments are rejected. Discovery sorts paths ordinal and skips
/// reparse points, so a linked directory can never be walked outside the asset root.
/// </para>
/// </remarks>
internal static class BakeSourcePath
{
    /// <summary>
    /// Converts a source-relative path to a canonical asset path by removing one matched registered
    /// processor extension.
    /// </summary>
    /// <param name="relativeSourcePath">The path relative to the pack root.</param>
    /// <param name="matchedExtension">
    /// The normalized extension the processor registry matched (or <c>toml</c> for a data source),
    /// without a leading dot; a compound extension such as <c>tar.gz</c> removes both segments.
    /// </param>
    /// <param name="assetPath">The canonical asset path when successful.</param>
    /// <param name="reason">The failure reason when unsuccessful.</param>
    /// <returns><see langword="true"/> when a canonical asset path was produced.</returns>
    internal static bool TryToAssetPath(
        string relativeSourcePath,
        string matchedExtension,
        out string assetPath,
        out string? reason)
    {
        assetPath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativeSourcePath))
        {
            reason = "the path is empty";
            return false;
        }

        if (Path.IsPathRooted(relativeSourcePath))
        {
            reason = "the path is rooted";
            return false;
        }

        if (string.IsNullOrWhiteSpace(matchedExtension))
        {
            reason = "the matched processor extension is empty";
            return false;
        }

        string normalized = relativeSourcePath.Replace('\\', '/').ToLowerInvariant();
        string suffix = "." + matchedExtension.ToLowerInvariant();
        if (!normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            reason = $"the path does not end with the matched extension '{suffix}'";
            return false;
        }

        string withoutSuffix = normalized[..^suffix.Length];
        if (!AssetPath.TryValidateCanonical(withoutSuffix, out reason))
        {
            return false;
        }

        assetPath = withoutSuffix;
        return true;
    }

    /// <summary>
    /// Makes a source path relative to the asset root.
    /// </summary>
    /// <param name="assetRoot">The asset-root directory.</param>
    /// <param name="sourcePath">The source file or directory path.</param>
    /// <param name="relativePath">The source-relative path with <c>/</c> separators.</param>
    /// <param name="reason">The failure reason when unsuccessful.</param>
    /// <returns><see langword="true"/> when the path is under the root.</returns>
    internal static bool TryMakeRelative(
        string assetRoot,
        string sourcePath,
        out string relativePath,
        out string? reason)
    {
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(assetRoot) || string.IsNullOrWhiteSpace(sourcePath))
        {
            reason = "a path is empty";
            return false;
        }

        string rootFull;
        string sourceFull;
        try
        {
            rootFull = Path.GetFullPath(assetRoot);
            sourceFull = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            reason = exception.Message;
            return false;
        }

        if (!IsUnderRoot(rootFull, sourceFull))
        {
            reason = "the path is outside the asset root";
            return false;
        }

        string relative = Path.GetRelativePath(rootFull, sourceFull);
        if (relative.Length == 0 || relative == ".")
        {
            reason = "the path is the asset root";
            return false;
        }

        relativePath = relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        reason = null;
        return true;
    }

    /// <summary>
    /// Resolves a source-relative path under the asset root and verifies containment on a separator
    /// boundary.
    /// </summary>
    /// <param name="assetRoot">The asset-root directory.</param>
    /// <param name="relativeSourcePath">The path relative to the asset root.</param>
    /// <param name="fullPath">The resolved absolute path when successful.</param>
    /// <param name="reason">The failure reason when unsuccessful.</param>
    /// <returns><see langword="true"/> when the resolved path is contained by the root.</returns>
    internal static bool TryResolveUnderRoot(
        string assetRoot,
        string relativeSourcePath,
        out string fullPath,
        out string? reason)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(assetRoot))
        {
            reason = "the asset root is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativeSourcePath))
        {
            reason = "the path is empty";
            return false;
        }

        if (Path.IsPathRooted(relativeSourcePath))
        {
            reason = "the path is rooted";
            return false;
        }

        if (HasParentSegment(relativeSourcePath))
        {
            reason = "the path contains a '..' segment";
            return false;
        }

        string rootFull;
        string candidate;
        try
        {
            rootFull = Path.GetFullPath(assetRoot);
            candidate = Path.GetFullPath(Path.Combine(rootFull, relativeSourcePath));
        }
        catch (Exception exception) when (IsPathFailure(exception))
        {
            reason = exception.Message;
            return false;
        }

        if (!IsUnderRoot(rootFull, candidate))
        {
            reason = "the path escapes the asset root";
            return false;
        }

        fullPath = candidate;
        reason = null;
        return true;
    }

    /// <summary>
    /// Tests whether a full path is the asset root or is contained by it.
    /// </summary>
    /// <param name="assetRoot">The asset-root directory.</param>
    /// <param name="fullPath">The full path to test.</param>
    /// <returns><see langword="true"/> when the path is contained on a separator boundary.</returns>
    internal static bool IsUnderRoot(string assetRoot, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetRoot));
        string candidate = Path.GetFullPath(fullPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (candidate.Equals(root, comparison))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Tests whether a filesystem entry is a reparse point (a symbolic link, junction, or similar).
    /// </summary>
    /// <param name="path">The existing path to test.</param>
    /// <returns><see langword="true"/> when the entry is a reparse point.</returns>
    internal static bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>
    /// Enumerates regular files under a root in ordinal path order, skipping reparse points.
    /// </summary>
    /// <param name="root">The directory to walk.</param>
    /// <returns>The full paths of regular files, sorted ordinal.</returns>
    internal static IReadOnlyList<string> EnumerateFilesSorted(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull))
        {
            return [];
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootFull);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception exception) when (IsEnumerationFailure(exception))
            {
                continue;
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsEnumerationFailure(exception))
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static bool IsPathFailure(Exception exception)
        => exception is ArgumentException or NotSupportedException or PathTooLongException;

    private static bool HasParentSegment(string path)
    {
        foreach (string segment in path.Split(['/', '\\'], StringSplitOptions.None))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnumerationFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;
}
