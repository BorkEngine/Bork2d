namespace Bork2d.Bake.Fonts;

/// <summary>
/// Identifies the font reader, TrueType/CFF outline reader, and atlas pipeline versions used by the
/// bake.
/// </summary>
/// <remarks>
/// <see cref="ConfigurationMarker"/> participates in the incremental configuration hash, so changing
/// the outline reader, upgrading StbTrueTypeSharp, or changing glyph selection, em derivation,
/// packing, or MSDF rasterization invalidates every cached font atlas instead of reusing bytes
/// produced by a different tool.
/// </remarks>
internal static class FontToolInfo
{
    /// <summary>The font processor and outline builder version, bumped when their semantics change.</summary>
    internal const string FontReaderVersion = "font-1";

    /// <summary>The selected TrueType/CFF outline reader package version.</summary>
    internal const string OutlineReaderVersion = "stbtruetype-1.26.13";

    /// <summary>The atlas selection, packing, and MSDF pipeline version.</summary>
    internal const string AtlasVersion = "atlas-1";

    /// <summary>
    /// Gets the marker recorded in the incremental configuration hash:
    /// <c>font-1/stbtruetype-1.26.13/atlas-1</c>.
    /// </summary>
    internal static string ConfigurationMarker { get; } =
        $"{FontReaderVersion}/{OutlineReaderVersion}/{AtlasVersion}";
}
