namespace Bork2d.Bake.Sources;

/// <summary>
/// One reference value found while binding a source: the member that produced it, the target path as
/// written, whether it is a raw <c>DataRef</c>, its source position, and the referenced CLR type for a
/// typed <c>AssetRef&lt;T&gt;</c>.
/// </summary>
/// <param name="MemberPath">The member path relative to the asset root, for example <c>items[2].icon</c>.</param>
/// <param name="ReferencePath">The canonical path as written in the source.</param>
/// <param name="IsData">Whether the reference targets a raw payload.</param>
/// <param name="Line">The 1-based source line, when known.</param>
/// <param name="Column">The 1-based source column, when known.</param>
/// <param name="TargetTypeName">
/// The referenced CLR type's full name for an <c>AssetRef&lt;T&gt;</c>, or <see langword="null"/> for
/// a <c>DataRef</c>.
/// </param>
internal readonly record struct BakeReferenceSite(
    string MemberPath,
    string ReferencePath,
    bool IsData,
    int? Line,
    int? Column,
    string? TargetTypeName);
