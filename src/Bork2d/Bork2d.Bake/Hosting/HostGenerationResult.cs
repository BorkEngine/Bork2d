namespace Bork2d.Bake.Hosting;

/// <summary>
/// The outcome of one <c>generate-host</c> invocation, for the CLI summary and for tests.
/// </summary>
/// <remarks>
/// A result is only produced when generation succeeded; any diagnostic failure returns
/// <see langword="null"/> from <see cref="HostGenerator.Generate"/> and the caller renders the
/// collected diagnostics.
/// </remarks>
internal sealed class HostGenerationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostGenerationResult"/> class.
    /// </summary>
    /// <param name="changed">Whether a file was written.</param>
    /// <param name="projectPath">The generated project path.</param>
    /// <param name="manifestPath">The generated manifest path.</param>
    /// <param name="types">The distinct mapped types in source discovery order.</param>
    /// <param name="references">The generated assembly references.</param>
    internal HostGenerationResult(
        bool changed,
        string projectPath,
        string manifestPath,
        IReadOnlyList<BakeMappedType> types,
        IReadOnlyList<BakeHostReference> references)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(manifestPath);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(references);

        Changed = changed;
        ProjectPath = projectPath;
        ManifestPath = manifestPath;
        Types = types;
        References = references;
    }

    /// <summary>Gets a value indicating whether a generated file was written.</summary>
    internal bool Changed { get; }

    /// <summary>Gets the generated project path.</summary>
    internal string ProjectPath { get; }

    /// <summary>Gets the generated manifest path.</summary>
    internal string ManifestPath { get; }

    /// <summary>Gets the distinct mapped types in source discovery order.</summary>
    internal IReadOnlyList<BakeMappedType> Types { get; }

    /// <summary>Gets the generated assembly references in ordinal order.</summary>
    internal IReadOnlyList<BakeHostReference> References { get; }
}
