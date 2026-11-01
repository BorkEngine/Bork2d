using Bork2d.Bake.Processors;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Baking;

/// <summary>
/// The validated, immutable configuration of one bake: the project root, the ordered packs, the
/// texture defaults, the explicitly registered data types, and the registered processors.
/// </summary>
/// <remarks>
/// <para>
/// A project is produced by <see cref="BakeProjectFactory"/> from a
/// <see cref="BakeProjectDefinition"/>; it is the only configuration model the pipeline sees. There is
/// no project descriptor, no generated host, and no common-assembly option. The first pack is the
/// main/base pack; every later pack is a DLC/overlay pack.
/// </para>
/// <para>
/// <see cref="AssetTypes"/> is the compile-time registration dictionary keyed by
/// <see cref="Type.FullName"/>. A data TOML's <c>[bork2d].type</c> selects one entry; an unregistered
/// name is a configuration diagnostic. Each registration also carries its semantic validators in
/// registration order; the pipeline runs them after binding and before processors.
/// <see cref="Processors"/> is the registration-order list and <see cref="ProcessorRegistry"/> is the
/// immutable normalized extension registry built from it; the pipeline runs those processors over the
/// discovered processor sources. Texture support is the built-in processor registered by
/// <c>UseDefaultProcessors()</c>; there is no separate texture pipeline.
/// </para>
/// </remarks>
internal sealed class BakeProject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProject"/> class.
    /// </summary>
    /// <param name="projectRoot">The absolute project root.</param>
    /// <param name="packs">The ordered packs; the first is the main pack.</param>
    /// <param name="textureFilter">The sampler filter for a texture sidecar created by default.</param>
    /// <param name="textureWrap">The sampler wrap mode for a texture sidecar created by default.</param>
    /// <param name="assetTypes">The explicitly registered data types keyed by full type name.</param>
    /// <param name="processors">The registered processors, in registration order.</param>
    /// <param name="processorRegistry">The immutable extension registry built from the processors.</param>
    /// <param name="useDefaultProcessors">Whether the default processor registration was requested.</param>
    internal BakeProject(
        string projectRoot,
        IReadOnlyList<BakePack> packs,
        BakeTextureFilter textureFilter,
        BakeTextureWrap textureWrap,
        IReadOnlyDictionary<string, BakeAssetTypeRegistration> assetTypes,
        IReadOnlyList<BakeProcessorRegistration> processors,
        BakeProcessorRegistry processorRegistry,
        bool useDefaultProcessors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(assetTypes);
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(processorRegistry);
        if (packs.Count == 0)
        {
            throw new ArgumentException("A project must configure at least one asset pack.", nameof(packs));
        }

        ProjectRoot = projectRoot;
        Packs = [.. packs];
        TextureFilter = textureFilter;
        TextureWrap = textureWrap;
        AssetTypes = new Dictionary<string, BakeAssetTypeRegistration>(
            assetTypes,
            StringComparer.Ordinal);
        Processors = [.. processors];
        ProcessorRegistry = processorRegistry;
        UseDefaultProcessors = useDefaultProcessors;
    }

    /// <summary>Gets the absolute project root every relative root and pack path is based on.</summary>
    internal string ProjectRoot { get; }

    /// <summary>Gets the ordered packs; the first entry is the main/base pack.</summary>
    internal IReadOnlyList<BakePack> Packs { get; }

    /// <summary>Gets the main/base pack (the first configured pack).</summary>
    internal BakePack MainPack => Packs[0];

    /// <summary>Gets the sampler filter for a texture sidecar created by default.</summary>
    internal BakeTextureFilter TextureFilter { get; }

    /// <summary>Gets the sampler wrap mode for a texture sidecar created by default.</summary>
    internal BakeTextureWrap TextureWrap { get; }

    /// <summary>Gets the explicitly registered data types keyed by full type name.</summary>
    internal IReadOnlyDictionary<string, BakeAssetTypeRegistration> AssetTypes { get; }

    /// <summary>Gets the processor registrations in registration order.</summary>
    internal IReadOnlyList<BakeProcessorRegistration> Processors { get; }

    /// <summary>Gets the immutable extension registry built from <see cref="Processors"/>.</summary>
    internal BakeProcessorRegistry ProcessorRegistry { get; }

    /// <summary>Gets a value indicating whether the default processor registration was requested.</summary>
    internal bool UseDefaultProcessors { get; }
}
