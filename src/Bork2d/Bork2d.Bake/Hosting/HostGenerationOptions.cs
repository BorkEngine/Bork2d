namespace Bork2d.Bake.Hosting;

/// <summary>
/// The inputs of one <c>bork2d-bake generate-host</c> invocation.
/// </summary>
/// <remarks>
/// CLI paths are already resolved against the process current directory by the caller; the
/// descriptor's own paths are resolved by <see cref="Project.ProjectDescriptorParser"/> against the
/// descriptor's directory. The configuration is <c>Debug</c> or <c>Release</c> and becomes the
/// generated project's default when a bare <c>dotnet build</c> does not pass one.
/// </remarks>
internal sealed class HostGenerationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostGenerationOptions"/> class.
    /// </summary>
    /// <param name="projectPath">The descriptor path.</param>
    /// <param name="commonAssemblyPath">The <c>Project.Common</c> assembly path.</param>
    /// <param name="outputDirectory">The directory the generated host is written to.</param>
    /// <param name="configuration">The default build configuration.</param>
    /// <param name="force">Whether to rewrite files even when they are unchanged.</param>
    internal HostGenerationOptions(
        string projectPath,
        string commonAssemblyPath,
        string outputDirectory,
        string configuration,
        bool force)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        ProjectPath = projectPath;
        CommonAssemblyPath = commonAssemblyPath;
        OutputDirectory = outputDirectory;
        Configuration = configuration;
        Force = force;
    }

    /// <summary>Gets the descriptor path.</summary>
    internal string ProjectPath { get; }

    /// <summary>Gets the <c>Project.Common</c> assembly path.</summary>
    internal string CommonAssemblyPath { get; }

    /// <summary>Gets the directory the generated host is written to.</summary>
    internal string OutputDirectory { get; }

    /// <summary>Gets the default build configuration.</summary>
    internal string Configuration { get; }

    /// <summary>Gets a value indicating whether unchanged files are rewritten.</summary>
    internal bool Force { get; }

    /// <summary>Tests whether a value is a supported configuration name.</summary>
    /// <param name="value">The configuration name.</param>
    /// <returns><see langword="true"/> for <c>Debug</c> or <c>Release</c>.</returns>
    internal static bool IsSupportedConfiguration(string? value)
        => string.Equals(value, "Debug", StringComparison.Ordinal)
            || string.Equals(value, "Release", StringComparison.Ordinal);
}
