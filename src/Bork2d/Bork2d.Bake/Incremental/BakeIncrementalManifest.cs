namespace Bork2d.Bake.Incremental;

/// <summary>
/// The schema-3 incremental bake manifest: the tool version, the configuration and shape-closure
/// hashes, every tracked input hash, the mode, every written pack output hash, and the aggregate
/// counts.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is a pure data model. <see cref="BakeManifestJson"/> owns its deterministic
/// serialization, <see cref="BakeManifestStore"/> owns the atomic read/write, and
/// <see cref="BakeIncrementalPlanner"/> owns the skip decision. Inputs are kept in ordinal display-path
/// order; outputs keep their configured pack order.
/// </para>
/// <para>
/// A manifest is all-or-nothing: there is no per-pack cache. Any different field, a missing output,
/// or an output whose content hash changed forces a full bake. Schema 2 (the M4 shape) is not
/// readable by this model and therefore also forces a full bake.
/// </para>
/// </remarks>
internal sealed class BakeIncrementalManifest
{
    /// <summary>The manifest schema version this build writes and reads.</summary>
    internal const int CurrentVersion = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeIncrementalManifest"/> class.
    /// </summary>
    /// <param name="toolVersion">The bake tool informational version.</param>
    /// <param name="configurationHash">The builder-configuration hash.</param>
    /// <param name="shapeClosureHash">The registered type/processor assembly closure hash.</param>
    /// <param name="mode">The bake mode name (<c>debug</c> or <c>release</c>).</param>
    /// <param name="inputs">The tracked inputs; sorted by display path here.</param>
    /// <param name="outputs">The written pack outputs in configured pack order.</param>
    /// <param name="assetCount">The aggregate shaped asset count.</param>
    /// <param name="textureCount">The aggregate texture count.</param>
    /// <param name="referenceCount">The aggregate reference-site count.</param>
    internal BakeIncrementalManifest(
        string toolVersion,
        string configurationHash,
        string shapeClosureHash,
        string mode,
        IReadOnlyList<BakeInputHash> inputs,
        IReadOnlyList<BakeOutputHash> outputs,
        int assetCount,
        int textureCount,
        int referenceCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(shapeClosureHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentOutOfRangeException.ThrowIfNegative(assetCount);
        ArgumentOutOfRangeException.ThrowIfNegative(textureCount);
        ArgumentOutOfRangeException.ThrowIfNegative(referenceCount);

        ToolVersion = toolVersion;
        ConfigurationHash = configurationHash;
        ShapeClosureHash = shapeClosureHash;
        Mode = mode;
        Inputs = [.. inputs.OrderBy(input => input.Path, StringComparer.Ordinal)];
        Outputs = [.. outputs];
        AssetCount = assetCount;
        TextureCount = textureCount;
        ReferenceCount = referenceCount;
    }

    /// <summary>Gets the manifest schema version (<see cref="CurrentVersion"/>).</summary>
    internal int Version => CurrentVersion;

    /// <summary>Gets the bake tool informational version that wrote the manifest.</summary>
    internal string ToolVersion { get; }

    /// <summary>Gets the builder-configuration hash.</summary>
    internal string ConfigurationHash { get; }

    /// <summary>Gets the registered type and processor assembly closure hash.</summary>
    internal string ShapeClosureHash { get; }

    /// <summary>Gets the bake mode name.</summary>
    internal string Mode { get; }

    /// <summary>Gets the tracked inputs in ordinal display-path order.</summary>
    internal IReadOnlyList<BakeInputHash> Inputs { get; }

    /// <summary>Gets the written pack outputs in configured pack order.</summary>
    internal IReadOnlyList<BakeOutputHash> Outputs { get; }

    /// <summary>Gets the aggregate shaped asset count.</summary>
    internal int AssetCount { get; }

    /// <summary>Gets the aggregate texture count.</summary>
    internal int TextureCount { get; }

    /// <summary>Gets the aggregate reference-site count.</summary>
    internal int ReferenceCount { get; }
}
