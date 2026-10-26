namespace Bork2d.Bake.Sources;

/// <summary>
/// One discovered custom-data TOML source and the mapping that covers it.
/// </summary>
/// <param name="RelativePath">The source path relative to the asset root, with <c>/</c> separators.</param>
/// <param name="FullPath">The absolute source path.</param>
/// <param name="AssetPath">The canonical asset path (lowercase, <c>/</c>, without <c>.toml</c>).</param>
/// <param name="TypeName">The mapped CLR type's full name.</param>
/// <param name="Prefix">The matched <c>[[types]]</c> prefix.</param>
internal sealed record BakeSource(
    string RelativePath,
    string FullPath,
    string AssetPath,
    string TypeName,
    string Prefix);
