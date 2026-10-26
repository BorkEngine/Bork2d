namespace Bork2d.Bake.Hosting;

/// <summary>
/// One validated <c>[[types]]</c> mapping as the generated host sees it.
/// </summary>
/// <param name="TypeName">The mapped CLR type's full name.</param>
/// <param name="SourcePrefix">The canonical source prefix.</param>
/// <param name="CSharpName">The type's name in generated C#, for example <c>global::MyGame.Data.GameConfig</c>.</param>
/// <param name="AssemblyPath">The absolute path of the assembly that defines the type.</param>
/// <param name="Kind">The pack-entry kind the mapping produces.</param>
internal readonly record struct BakeMappedType(
    string TypeName,
    string SourcePrefix,
    string CSharpName,
    string AssemblyPath,
    BakeAssetKind Kind);
