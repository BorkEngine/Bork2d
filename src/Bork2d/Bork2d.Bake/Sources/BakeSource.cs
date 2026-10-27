using Bork2d.Bake.Project;

namespace Bork2d.Bake.Sources;

/// <summary>
/// One discovered custom-data TOML source and its own <c>[bork2d].type</c> declaration (D-M4-13),
/// together with the <c>[[assets]]</c> pack it was discovered under (D-M4-14).
/// </summary>
/// <param name="Pack">The owning descriptor pack entry.</param>
/// <param name="RelativePath">The source path relative to the pack's asset root, with <c>/</c> separators.</param>
/// <param name="FullPath">The absolute source path.</param>
/// <param name="AssetPath">The canonical asset path (lowercase, <c>/</c>, without <c>.toml</c>).</param>
/// <param name="TypeName">The declared CLR type's full name.</param>
/// <param name="TypeLine">The optional 1-based line of the <c>type</c> value.</param>
/// <param name="TypeColumn">The optional 1-based column of the <c>type</c> value.</param>
internal sealed record BakeSource(
    ProjectPack Pack,
    string RelativePath,
    string FullPath,
    string AssetPath,
    string TypeName,
    int? TypeLine,
    int? TypeColumn);
