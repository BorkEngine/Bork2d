namespace Bork2d.Bake.Project;

/// <summary>
/// The validated project descriptor from <c>bork2d.project.toml</c> (D-M4-14).
/// </summary>
/// <remarks>
/// <para>
/// A project declares one or more ordered <c>[[assets]]</c> entries. The first entry is the main/base
/// pack; later entries are DLC/overlay packs. Each entry owns its asset root and its pack output
/// path, so a project no longer has a single <c>[assets].root</c>, a project <c>name</c>, or a
/// <c>[bake].pack</c>.
/// </para>
/// <para>
/// Asset roots are absolute paths resolved against the descriptor's directory, never the process
/// current directory. Pack paths stay relative to the output root (the descriptor directory by
/// default) so the same descriptor can bake under <c>bin/</c> for MSBuild; see
/// <see cref="Baking.BakeOutputLayout"/>.
/// </para>
/// <para>
/// The descriptor does not declare types: every custom-data TOML declares its own type in its
/// reserved <c>[bork2d]</c> table (D-M4-13). Mapped type existence and shape validation belong to the
/// generated host, which scans the roots first.
/// </para>
/// </remarks>
internal sealed class ProjectDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectDescriptor"/> class.
    /// </summary>
    /// <param name="descriptorPath">The absolute descriptor file path.</param>
    /// <param name="packs">The ordered asset packs; the first is the main pack.</param>
    /// <param name="hostPath">The absolute generated-host directory.</param>
    /// <param name="textureFilter">The default texture filter.</param>
    /// <param name="textureWrap">The default texture wrap mode.</param>
    internal ProjectDescriptor(
        string descriptorPath,
        IReadOnlyList<ProjectPack> packs,
        string hostPath,
        BakeTextureFilter textureFilter,
        BakeTextureWrap textureWrap)
    {
        ArgumentNullException.ThrowIfNull(descriptorPath);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(hostPath);
        if (packs.Count == 0)
        {
            throw new ArgumentException("A project must declare at least one asset pack.", nameof(packs));
        }

        DescriptorPath = descriptorPath;
        Packs = [.. packs];
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

    /// <summary>Gets the ordered asset packs; the first entry is the main/base pack.</summary>
    internal IReadOnlyList<ProjectPack> Packs { get; }

    /// <summary>Gets the main/base pack (the first <c>[[assets]]</c> entry).</summary>
    internal ProjectPack MainPack => Packs[0];

    /// <summary>Gets the absolute generated-host directory.</summary>
    internal string HostPath { get; }

    /// <summary>Gets the default sampler filter for texture imports.</summary>
    internal BakeTextureFilter TextureFilter { get; }

    /// <summary>Gets the default sampler wrap mode for texture imports.</summary>
    internal BakeTextureWrap TextureWrap { get; }
}
