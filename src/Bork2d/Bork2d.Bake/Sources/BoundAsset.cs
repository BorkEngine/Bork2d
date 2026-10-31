namespace Bork2d.Bake.Sources;

/// <summary>
/// One source file bound to its mapped type, ready for pack composition.
/// </summary>
/// <remarks>
/// The model is what WP4 composes and validates: the canonical asset path, the mapped type, the
/// bound value, the source path used in diagnostics, and the reference sites (with their value
/// spans). WP2 does not validate references against the baked set and never writes a pack.
/// </remarks>
internal sealed class BoundAsset
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BoundAsset"/> class.
    /// </summary>
    /// <param name="assetPath">The canonical asset path.</param>
    /// <param name="typeName">The mapped type's full name.</param>
    /// <param name="value">The bound typed value.</param>
    /// <param name="sourcePath">The project-relative or absolute source path used in diagnostics.</param>
    /// <param name="relativeSourcePath">The source path relative to the asset root.</param>
    /// <param name="references">The reference sites found while binding.</param>
    internal BoundAsset(
        string assetPath,
        string typeName,
        object value,
        string sourcePath,
        string relativeSourcePath,
        IReadOnlyList<BakeReferenceSite> references)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeSourcePath);
        ArgumentNullException.ThrowIfNull(references);

        AssetPath = assetPath;
        TypeName = typeName;
        Value = value;
        SourcePath = sourcePath;
        RelativeSourcePath = relativeSourcePath;
        References = references;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string AssetPath { get; }

    /// <summary>Gets the mapped type's full name.</summary>
    internal string TypeName { get; }

    /// <summary>Gets the bound typed value.</summary>
    internal object Value { get; }

    /// <summary>Gets the source path used in diagnostics.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the source path relative to the asset root.</summary>
    internal string RelativeSourcePath { get; }

    /// <summary>Gets the reference sites found while binding.</summary>
    internal IReadOnlyList<BakeReferenceSite> References { get; }
}
