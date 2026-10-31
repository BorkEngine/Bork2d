namespace Bork2d.Bake.Incremental;

/// <summary>
/// The fixed project-relative locations of the incremental bake state.
/// </summary>
/// <remarks>
/// The manifest is internal build state: it lives under the project's <c>obj</c> directory, is never
/// committed, and is removed by <c>clean</c>. Keeping the paths in one place means the planner, the
/// command line, and the tests agree without a project-descriptor property.
/// </remarks>
internal static class BakeManifestPaths
{
    /// <summary>Gets the incremental bake state directory under a project root.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <returns>The absolute <c>obj/bork2d/bake</c> directory.</returns>
    internal static string Directory(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return Path.Combine(projectRoot, "obj", "bork2d", "bake");
    }

    /// <summary>Gets the incremental manifest path under a project root.</summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <returns>The absolute <c>obj/bork2d/bake/manifest.json</c> path.</returns>
    internal static string ManifestPath(string projectRoot)
        => Path.Combine(Directory(projectRoot), "manifest.json");
}
