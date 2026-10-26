namespace Bork2d.Bake.Incremental;

/// <summary>
/// Computes the internal, non-committed incremental artifact paths under the descriptor directory.
/// </summary>
/// <remarks>
/// The M4 layout is <c>&lt;descriptor directory&gt;/obj/bork2d/bake/</c> for both the bake manifest
/// and the host-generation state (M4 §5.4/§10.3). The paths are derived from the descriptor, never
/// from the process current directory, so a CLI invocation from any directory uses one state.
/// </remarks>
internal static class BakeManifestPaths
{
    /// <summary>Gets the manifest file name.</summary>
    internal const string ManifestFileName = "manifest.json";

    /// <summary>Gets the host-generation state file name.</summary>
    internal const string HostGenerationStateFileName = "host-generation.json";

    /// <summary>Gets the incremental artifact directory for a descriptor directory.</summary>
    /// <param name="descriptorDirectory">The directory that contains the project descriptor.</param>
    /// <returns>The absolute <c>obj/bork2d/bake</c> directory.</returns>
    internal static string DirectoryPath(string descriptorDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorDirectory);
        return Path.Combine(Path.GetFullPath(descriptorDirectory), "obj", "bork2d", "bake");
    }

    /// <summary>Gets the bake manifest path for a descriptor directory.</summary>
    /// <param name="descriptorDirectory">The directory that contains the project descriptor.</param>
    /// <returns>The absolute manifest path.</returns>
    internal static string ManifestPath(string descriptorDirectory)
        => Path.Combine(DirectoryPath(descriptorDirectory), ManifestFileName);

    /// <summary>Gets the host-generation state path for a descriptor directory.</summary>
    /// <param name="descriptorDirectory">The directory that contains the project descriptor.</param>
    /// <returns>The absolute host-generation state path.</returns>
    internal static string HostGenerationStatePath(string descriptorDirectory)
        => Path.Combine(DirectoryPath(descriptorDirectory), HostGenerationStateFileName);
}
