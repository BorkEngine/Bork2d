namespace Bork2d.Bake.Fonts;

/// <summary>
/// The stable font parse, selection, atlas, and MSDF bake diagnostic codes.
/// </summary>
/// <remarks>
/// The codes occupy the <c>BK9300-BK9399</c> range. The shared <c>BK2xxx</c> binder diagnostics still
/// cover TOML syntax, unknown keys, value types, and enum names in a font sidecar.
/// </remarks>
internal static class FontDiagnosticCodes
{
    /// <summary>
    /// The font source cannot be read, is not a recognized TTF/OTF container, or its outline tables
    /// cannot be initialized.
    /// </summary>
    internal const string SourceUnreadable = "BK9301";

    /// <summary>
    /// The sidecar selection or bake options are invalid: an empty selection, a malformed, inverted,
    /// surrogate, or out-of-range codepoint or range, or an invalid resolution, range, or padding
    /// value that leaves no usable atlas.
    /// </summary>
    internal const string SelectionInvalid = "BK9302";

    /// <summary>
    /// A selected codepoint is absent from the font. The bake continues and emits the other glyphs.
    /// </summary>
    internal const string GlyphMissing = "BK9303";

    /// <summary>
    /// A glyph's outline or MSDF field cannot be generated or validated: an empty or all-inside/
    /// all-outside field, a non-finite channel, an invalid outline, or a sign mismatch against the
    /// exact shape distance.
    /// </summary>
    internal const string FieldInvalid = "BK9304";

    /// <summary>
    /// The selected glyphs do not fit the atlas at the minimum em size, or the selection has no
    /// usable glyph. The message names the glyph count and the atlas size.
    /// </summary>
    internal const string AtlasDoesNotFit = "BK9305";

    /// <summary>The KTX2 atlas payload could not be encoded or written.</summary>
    internal const string PayloadWriteFailed = "BK9306";
}
