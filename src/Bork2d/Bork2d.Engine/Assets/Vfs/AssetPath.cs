namespace Bork2d.Engine.Assets.Vfs;

/// <summary>
/// Normalizes and validates canonical asset paths.
/// </summary>
/// <remarks>
/// Canonical paths are runtime asset keys: lowercase ASCII, <c>/</c> separators, no leading or
/// trailing slash, no empty segments, no <c>.</c>/<c>..</c> segments, and segments restricted to
/// lowercase letters, digits, <c>_</c>, and <c>-</c>. Source extensions are therefore rejected.
/// Public inputs are normalized first (<c>\</c> to <c>/</c> and invariant lowercase); pack paths
/// are validated without normalization.
/// </remarks>
internal static class AssetPath
{
    /// <summary>
    /// Normalizes a public path input and validates the canonical result.
    /// </summary>
    /// <param name="path">The caller-supplied path.</param>
    /// <returns>The canonical path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a valid asset path.</exception>
    internal static string Normalize(string? path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (!TryValidateCanonical(normalized, out string? reason))
        {
            throw new ArgumentException(
                $"Invalid asset path '{path}': {reason}.",
                nameof(path));
        }

        return normalized;
    }

    /// <summary>
    /// Tests whether a path already has the canonical form.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is canonical.</returns>
    internal static bool IsCanonical(string? path) => TryValidateCanonical(path, out _);

    /// <summary>
    /// Validates a path without normalizing it.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="reason">The validation failure reason, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the path is canonical.</returns>
    internal static bool TryValidateCanonical(string? path, out string? reason)
    {
        if (path is null)
        {
            reason = "the path is null";
            return false;
        }

        if (path.Length == 0)
        {
            reason = "the path is empty";
            return false;
        }

        if (path[0] == '/' || path[^1] == '/')
        {
            reason = "the path has a leading or trailing '/'";
            return false;
        }

        int segmentStart = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/')
            {
                continue;
            }

            int segmentLength = i - segmentStart;
            if (segmentLength == 0)
            {
                reason = "the path contains an empty segment";
                return false;
            }

            if (segmentLength == 1 && path[segmentStart] == '.')
            {
                reason = "the path contains a '.' segment";
                return false;
            }

            if (segmentLength == 2
                && path[segmentStart] == '.'
                && path[segmentStart + 1] == '.')
            {
                reason = "the path contains a '..' segment";
                return false;
            }

            for (int j = segmentStart; j < i; j++)
            {
                char character = path[j];
                if (!IsCanonicalCharacter(character))
                {
                    reason = $"the path contains the invalid character '{character}'";
                    return false;
                }
            }

            segmentStart = i + 1;
        }

        reason = null;
        return true;
    }

    private static bool IsCanonicalCharacter(char character)
        => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_'
            or '-';
}
