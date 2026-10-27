namespace Bork2d.Bake.Incremental;

/// <summary>
/// The hashed inputs and every pack output recorded by the incremental bake manifest.
/// </summary>
/// <remarks>
/// <para>
/// This is the internal, versioned model behind <c>obj/bork2d/bake/manifest.json</c> (M4 §10.3,
/// Q-09). It is never committed and never packed. <see cref="Inputs"/> is sorted ordinal by path and
/// <see cref="Outputs"/> follows descriptor order (main first), so a manifest is structurally
/// deterministic; every hash is XXH3-128 (32 uppercase hex characters) with explicit field framing
/// (Q-09).
/// </para>
/// <para>
/// The manifest records everything the skip decision compares: the descriptor, every tracked source
/// across every pack root, the game-side assembly closure next to the generated host, the generated
/// host assembly, the tool version, the mode, every output pack path with its hash, and the aggregate
/// counts so an up-to-date run can report the same summary without baking.
/// </para>
/// </remarks>
internal sealed class BakeIncrementalManifest
{
    /// <summary>The current manifest schema version.</summary>
    internal const int CurrentVersion = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeIncrementalManifest"/> class.
    /// </summary>
    /// <param name="version">The manifest schema version.</param>
    /// <param name="toolVersion">The bake tool version.</param>
    /// <param name="descriptorHash">The descriptor file hash.</param>
    /// <param name="inputs">The hashed source inputs; sorted ordinal by path.</param>
    /// <param name="commonAssemblyHash">
    /// The hash of the game-side assembly closure next to the generated host (Project.Common and any
    /// data libraries it references).
    /// </param>
    /// <param name="hostHash">The generated bake-host assembly hash.</param>
    /// <param name="mode">The pack compression mode name (<c>debug</c> or <c>release</c>).</param>
    /// <param name="outputs">The produced pack paths and hashes in descriptor order.</param>
    /// <param name="assetCount">The aggregate number of bound custom-data assets.</param>
    /// <param name="textureCount">The aggregate number of baked textures.</param>
    /// <param name="referenceCount">The aggregate number of reference sites validated.</param>
    internal BakeIncrementalManifest(
        int version,
        string toolVersion,
        string descriptorHash,
        IEnumerable<BakeInputHash> inputs,
        string commonAssemblyHash,
        string hostHash,
        string mode,
        IEnumerable<BakeOutputHash> outputs,
        int assetCount,
        int textureCount,
        int referenceCount)
    {
        ArgumentNullException.ThrowIfNull(toolVersion);
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(commonAssemblyHash);
        ArgumentNullException.ThrowIfNull(hostHash);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentOutOfRangeException.ThrowIfNegative(assetCount);
        ArgumentOutOfRangeException.ThrowIfNegative(textureCount);
        ArgumentOutOfRangeException.ThrowIfNegative(referenceCount);

        Version = version;
        ToolVersion = toolVersion;
        DescriptorHash = descriptorHash;
        Inputs = [.. inputs.OrderBy(input => input.Path, StringComparer.Ordinal)];
        CommonAssemblyHash = commonAssemblyHash;
        HostHash = hostHash;
        Mode = mode;
        Outputs = [.. outputs];
        AssetCount = assetCount;
        TextureCount = textureCount;
        ReferenceCount = referenceCount;
    }

    /// <summary>Gets the manifest schema version.</summary>
    internal int Version { get; }

    /// <summary>Gets the bake tool version.</summary>
    internal string ToolVersion { get; }

    /// <summary>Gets the descriptor file hash.</summary>
    internal string DescriptorHash { get; }

    /// <summary>Gets the hashed source inputs, sorted ordinal by path.</summary>
    internal IReadOnlyList<BakeInputHash> Inputs { get; }

    /// <summary>Gets the hash of the game-side assembly closure next to the generated host.</summary>
    internal string CommonAssemblyHash { get; }

    /// <summary>Gets the generated bake-host assembly hash.</summary>
    internal string HostHash { get; }

    /// <summary>Gets the pack compression mode name (<c>debug</c> or <c>release</c>).</summary>
    internal string Mode { get; }

    /// <summary>Gets the produced pack paths and hashes in descriptor order.</summary>
    internal IReadOnlyList<BakeOutputHash> Outputs { get; }

    /// <summary>Gets the aggregate number of bound custom-data assets.</summary>
    internal int AssetCount { get; }

    /// <summary>Gets the aggregate number of baked textures.</summary>
    internal int TextureCount { get; }

    /// <summary>Gets the aggregate number of reference sites validated.</summary>
    internal int ReferenceCount { get; }
}
