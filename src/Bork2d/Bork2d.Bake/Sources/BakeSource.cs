namespace Bork2d.Bake.Sources;

/// <summary>
/// One discovered custom-data TOML source and its own <c>[bork2d].type</c> declaration (D-M4-13).
/// </summary>
/// <param name="RelativePath">The source path relative to the asset root, with <c>/</c> separators.</param>
/// <param name="FullPath">The absolute source path.</param>
/// <param name="AssetPath">The canonical asset path (lowercase, <c>/</c>, without <c>.toml</c>).</param>
/// <param name="TypeName">The declared CLR type's full name.</param>
/// <param name="TypeLine">The optional 1-based line of the <c>type</c> value.</param>
/// <param name="TypeColumn">The optional 1-based column of the <c>type</c> value.</param>
internal sealed record BakeSource(
    string RelativePath,
    string FullPath,
    string AssetPath,
    string TypeName,
    int? TypeLine,
    int? TypeColumn);
