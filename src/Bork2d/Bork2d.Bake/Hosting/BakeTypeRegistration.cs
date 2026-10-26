namespace Bork2d.Bake.Hosting;

/// <summary>
/// One type registration in a generated bake host manifest.
/// </summary>
/// <param name="TypeName">The mapped CLR type's full name.</param>
/// <param name="SourcePrefix">The canonical source prefix the type handles.</param>
/// <param name="Kind">The pack-entry kind the type produces.</param>
internal readonly record struct BakeTypeRegistration(
    string TypeName,
    string SourcePrefix,
    BakeAssetKind Kind);
