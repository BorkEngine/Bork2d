namespace Bork2d.Bake.Sources;

/// <summary>
/// The metadata outcome for one TOML source, used to distinguish data, sidecars, and malformed input.
/// </summary>
internal enum BakeSourceMetadataKind
{
    /// <summary>The document has no root <c>bork2d</c> table.</summary>
    Untyped,

    /// <summary>The document has a valid root <c>[bork2d]</c> table with a <c>type</c> declaration.</summary>
    Typed,

    /// <summary>The document has a root <c>bork2d</c> key that is not a valid metadata table.</summary>
    Invalid,

    /// <summary>The document is not valid TOML, so its metadata cannot be read.</summary>
    Unreadable,
}

/// <summary>
/// The result of reading a source's <c>[bork2d]</c> metadata.
/// </summary>
/// <param name="Kind">The metadata outcome.</param>
/// <param name="TypeName">The declared type name when <paramref name="Kind"/> is <c>Typed</c>.</param>
/// <param name="TypeLine">The 1-based line of the <c>type</c> value, when known.</param>
/// <param name="TypeColumn">The 1-based column of the <c>type</c> value, when known.</param>
internal readonly record struct BakeSourceMetadata(
    BakeSourceMetadataKind Kind,
    string? TypeName,
    int? TypeLine,
    int? TypeColumn);
