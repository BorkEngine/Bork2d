namespace Bork2d.Bake.Incremental;

/// <summary>
/// The inputs and output identity recorded after a successful <c>generate-host</c> run.
/// </summary>
/// <remarks>
/// The state lives next to the bake manifest under <c>obj/bork2d/bake/</c>. It is internal and never
/// committed. A match requires the descriptor hash, the <c>Project.Common</c> assembly hash, the
/// generator version, the configuration, the output directory, and the content hash of both generated
/// files to be unchanged; the caller additionally requires both files to exist.
/// </remarks>
internal sealed class BakeHostGenerationState
{
    /// <summary>The current state schema version.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeHostGenerationState"/> class.
    /// </summary>
    /// <param name="version">The state schema version.</param>
    /// <param name="generatorVersion">The host generator (tooling) version.</param>
    /// <param name="descriptorHash">The descriptor file hash.</param>
    /// <param name="commonAssemblyHash">The <c>Project.Common</c> assembly hash.</param>
    /// <param name="configuration">The generated project's default configuration.</param>
    /// <param name="outputDirectory">The absolute host output directory.</param>
    /// <param name="projectPath">The absolute generated project path.</param>
    /// <param name="manifestPath">The absolute generated manifest path.</param>
    /// <param name="generatedHash">The content hash of both generated files.</param>
    internal BakeHostGenerationState(
        int version,
        string generatorVersion,
        string descriptorHash,
        string commonAssemblyHash,
        string configuration,
        string outputDirectory,
        string projectPath,
        string manifestPath,
        string generatedHash)
    {
        ArgumentNullException.ThrowIfNull(generatorVersion);
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(commonAssemblyHash);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(manifestPath);
        ArgumentNullException.ThrowIfNull(generatedHash);

        Version = version;
        GeneratorVersion = generatorVersion;
        DescriptorHash = descriptorHash;
        CommonAssemblyHash = commonAssemblyHash;
        Configuration = configuration;
        OutputDirectory = outputDirectory;
        ProjectPath = projectPath;
        ManifestPath = manifestPath;
        GeneratedHash = generatedHash;
    }

    /// <summary>Gets the state schema version.</summary>
    internal int Version { get; }

    /// <summary>Gets the host generator (tooling) version.</summary>
    internal string GeneratorVersion { get; }

    /// <summary>Gets the descriptor file hash.</summary>
    internal string DescriptorHash { get; }

    /// <summary>Gets the <c>Project.Common</c> assembly hash.</summary>
    internal string CommonAssemblyHash { get; }

    /// <summary>Gets the generated project's default configuration.</summary>
    internal string Configuration { get; }

    /// <summary>Gets the absolute host output directory.</summary>
    internal string OutputDirectory { get; }

    /// <summary>Gets the absolute generated project path.</summary>
    internal string ProjectPath { get; }

    /// <summary>Gets the absolute generated manifest path.</summary>
    internal string ManifestPath { get; }

    /// <summary>Gets the content hash of both generated files.</summary>
    internal string GeneratedHash { get; }
}
