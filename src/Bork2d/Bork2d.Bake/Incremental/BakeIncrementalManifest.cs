namespace Bork2d.Bake.Incremental;

/// <summary>
/// The hashed inputs recorded by the incremental bake manifest.
/// </summary>
/// <remarks>
/// This is a model only: manifest serialization, reading, and skip/force behavior belong to WP5.
/// <see cref="Inputs"/> is sorted ordinal by path so a manifest is structurally deterministic, and
/// the hashes are XXH3-128 (Q-09) rather than SHA-256.
/// </remarks>
internal sealed class BakeIncrementalManifest
{
    /// <summary>The current manifest schema version.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeIncrementalManifest"/> class.
    /// </summary>
    /// <param name="version">The manifest schema version.</param>
    /// <param name="toolVersion">The bake tool version.</param>
    /// <param name="descriptorHash">The descriptor file hash.</param>
    /// <param name="inputs">The hashed source inputs; sorted ordinal by path.</param>
    /// <param name="commonAssemblyHash">The <c>Project.Common</c> assembly hash.</param>
    /// <param name="hostHash">The generated bake-host hash.</param>
    /// <param name="mode">The pack compression mode name.</param>
    /// <param name="outputHash">The produced pack hash.</param>
    internal BakeIncrementalManifest(
        int version,
        string toolVersion,
        string descriptorHash,
        IEnumerable<BakeInputHash> inputs,
        string commonAssemblyHash,
        string hostHash,
        string mode,
        string outputHash)
    {
        ArgumentNullException.ThrowIfNull(toolVersion);
        ArgumentNullException.ThrowIfNull(descriptorHash);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(commonAssemblyHash);
        ArgumentNullException.ThrowIfNull(hostHash);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(outputHash);

        Version = version;
        ToolVersion = toolVersion;
        DescriptorHash = descriptorHash;
        Inputs = [.. inputs.OrderBy(input => input.Path, StringComparer.Ordinal)];
        CommonAssemblyHash = commonAssemblyHash;
        HostHash = hostHash;
        Mode = mode;
        OutputHash = outputHash;
    }

    /// <summary>Gets the manifest schema version.</summary>
    internal int Version { get; }

    /// <summary>Gets the bake tool version.</summary>
    internal string ToolVersion { get; }

    /// <summary>Gets the descriptor file hash.</summary>
    internal string DescriptorHash { get; }

    /// <summary>Gets the hashed source inputs, sorted ordinal by path.</summary>
    internal IReadOnlyList<BakeInputHash> Inputs { get; }

    /// <summary>Gets the <c>Project.Common</c> assembly hash.</summary>
    internal string CommonAssemblyHash { get; }

    /// <summary>Gets the generated bake-host hash.</summary>
    internal string HostHash { get; }

    /// <summary>Gets the pack compression mode name.</summary>
    internal string Mode { get; }

    /// <summary>Gets the produced pack hash.</summary>
    internal string OutputHash { get; }
}
