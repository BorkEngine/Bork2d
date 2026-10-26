namespace Bork2d.Bake.Project;

/// <summary>
/// Maps a canonical source prefix to the CLR type name that bakes it.
/// </summary>
/// <param name="Prefix">
/// The canonical asset-path prefix relative to the asset root (lowercase, <c>/</c> separated).
/// </param>
/// <param name="TypeName">The mapped type's full name.</param>
/// <remarks>
/// A prefix matches an asset path when it is equal to the path or the path continues with a
/// <c>/</c> separator, so <c>data</c> matches <c>data</c> and <c>data/config</c> but never
/// <c>database</c>. The longest matching prefix wins; duplicate exact prefixes are descriptor
/// errors. Type existence and <c>IShapeable&lt;T&gt;</c> are validated at host generation (WP2),
/// not here.
/// </remarks>
internal sealed record ProjectTypeMapping(string Prefix, string TypeName);
