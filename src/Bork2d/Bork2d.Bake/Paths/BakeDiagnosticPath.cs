namespace Bork2d.Bake.Paths;

/// <summary>
/// Renders a source path for diagnostics in the documented form: project-relative with <c>/</c>
/// separators when the file is under the project directory, absolute otherwise.
/// </summary>
/// <remarks>
/// The descriptor's own diagnostics use the descriptor path; source diagnostics use this helper so a
/// diagnostic reads <c>Assets/data/enemies/grunt.toml(12,17)</c> and stays stable across machines.
/// </remarks>
internal static class BakeDiagnosticPath
{
    /// <summary>Renders a source file path for a diagnostic.</summary>
    /// <param name="projectDirectory">The directory that contains the project descriptor.</param>
    /// <param name="fullPath">The absolute source path.</param>
    /// <returns>The project-relative path when under the project directory, otherwise the input.</returns>
    internal static string ForSource(string projectDirectory, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!BakeSourcePath.IsUnderRoot(projectDirectory, fullPath))
        {
            return fullPath;
        }

        string relative = Path.GetRelativePath(Path.GetFullPath(projectDirectory), Path.GetFullPath(fullPath));
        return relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
