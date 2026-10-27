namespace Bork2d.Bake.Project;

/// <summary>
/// The validated project descriptor from <c>bork2d.project.toml</c>.
/// </summary>
/// <remarks>
/// <para>
/// All paths are absolute. Descriptor-relative paths were resolved against the descriptor's
/// directory, never the process current directory.
/// </para>
/// <para>
/// The descriptor no longer declares types: every custom-data TOML declares its own type in its
/// reserved <c>[bork2d]</c> table (D-M4-13). Mapped type existence and shape validation belong to the
/// generated host, which scans the sources first.
/// </para>
/// </remarks>
internal sealed class ProjectDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectDescriptor"/> class.
    /// </summary>
    /// <param name="descriptorPath">The absolute descriptor file path.</param>
    /// <param name="name">The optional project name.</param>
    /// <param name="assetsRoot">The absolute asset-root directory.</param>
    /// <param name="packPath">The absolute pack output path.</param>
    /// <param name="hostPath">The absolute generated-host directory.</param>
    /// <param name="textureFilter">The default texture filter.</param>
    /// <param name="textureWrap">The default texture wrap mode.</param>
    internal ProjectDescriptor(
        string descriptorPath,
        string? name,
        string assetsRoot,
        string packPath,
        string hostPath,
        BakeTextureFilter textureFilter,
        BakeTextureWrap textureWrap)
    {
        ArgumentNullException.ThrowIfNull(descriptorPath);
        ArgumentNullException.ThrowIfNull(assetsRoot);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(hostPath);

        DescriptorPath = descriptorPath;
        Name = name;
        AssetsRoot = assetsRoot;
        PackPath = packPath;
        HostPath = hostPath;
        TextureFilter = textureFilter;
        TextureWrap = textureWrap;
    }

    /// <summary>Gets the absolute descriptor file path.</summary>
    internal string DescriptorPath { get; }

    /// <summary>Gets the descriptor's directory; every relative descriptor path resolves here.</summary>
    internal string DescriptorDirectory
        => Path.GetDirectoryName(DescriptorPath) is { Length: > 0 } directory
            ? directory
            : DescriptorPath;

    /// <summary>Gets the optional project name.</summary>
    internal string? Name { get; }

    /// <summary>Gets the absolute asset-root directory.</summary>
    internal string AssetsRoot { get; }

    /// <summary>Gets the absolute pack output path.</summary>
    internal string PackPath { get; }

    /// <summary>Gets the absolute generated-host directory.</summary>
    internal string HostPath { get; }

    /// <summary>Gets the default sampler filter for texture imports.</summary>
    internal BakeTextureFilter TextureFilter { get; }

    /// <summary>Gets the default sampler wrap mode for texture imports.</summary>
    internal BakeTextureWrap TextureWrap { get; }
}
