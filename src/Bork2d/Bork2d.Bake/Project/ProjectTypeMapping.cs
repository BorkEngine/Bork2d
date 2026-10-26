namespace Bork2d.Bake.Project;

/// <summary>
/// Maps a canonical source prefix to the CLR type name that bakes it.
/// </summary>
/// <param name="Prefix">
/// The canonical asset-path prefix relative to the asset root (lowercase, <c>/</c> separated).
/// </param>
/// <param name="TypeName">The mapped type's full name.</param>
/// <remarks>
/// <para>
/// A prefix matches an asset path when it is equal to the path or the path continues with a
/// <c>/</c> separator, so <c>data</c> matches <c>data</c> and <c>data/config</c> but never
/// <c>database</c>. The longest matching prefix wins; duplicate exact prefixes are descriptor
/// errors. Type existence and <c>IShapeable&lt;T&gt;</c> are validated at host generation (WP2),
/// not here.
/// </para>
/// <para>
/// The optional 1-based <see cref="TypeLine"/>/<see cref="TypeColumn"/> and
/// <see cref="PrefixLine"/>/<see cref="PrefixColumn"/> locate the descriptor values, so
/// type-resolution diagnostics point at the <c>[[types]]</c> entry that produced them.
/// </para>
/// </remarks>
internal sealed record ProjectTypeMapping(string Prefix, string TypeName)
{
    /// <summary>Gets the 1-based line of the <c>type</c> value, when known.</summary>
    internal int? TypeLine { get; init; }

    /// <summary>Gets the 1-based column of the <c>type</c> value, when known.</summary>
    internal int? TypeColumn { get; init; }

    /// <summary>Gets the 1-based line of the <c>prefix</c> value, when known.</summary>
    internal int? PrefixLine { get; init; }

    /// <summary>Gets the 1-based column of the <c>prefix</c> value, when known.</summary>
    internal int? PrefixColumn { get; init; }
}
