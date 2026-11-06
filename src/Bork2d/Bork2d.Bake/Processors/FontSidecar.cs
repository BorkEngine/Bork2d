using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The settings sidecar of the built-in font processor: the fixed atlas resolution, the MSDF distance
/// spread, the extra margin, and the codepoint selection.
/// </summary>
/// <remarks>
/// <para>
/// Every member is optional so an existing sidecar can override one setting and inherit the rest from
/// the defaults. A font source without a sidecar gets one created with all five explicit default
/// values on the first bake.
/// </para>
/// <para>
/// The property names are the canonical camelCase keys <c>resolution</c>, <c>range</c>, <c>padding</c>,
/// <c>ranges</c>, and <c>glyphs</c>. <c>resolution</c> is the fixed square atlas side length in
/// pixels and must be in <c>1..8192</c>; <c>range</c> is the distance spread in pixels and must be
/// positive; <c>padding</c> is the extra margin in pixels beyond the range and must not be negative.
/// <c>ranges</c> holds inclusive <c>U+XXXX</c>/<c>U+XXXX-U+YYYY</c> ranges and <c>glyphs</c> holds
/// single <c>U+XXXX</c> values; the bake combines, deduplicates, and sorts them, and rejects a
/// malformed, inverted, surrogate, or out-of-range value. An absent key falls back to its default; a
/// present key always overrides it.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class FontSidecar
{
    /// <summary>
    /// Gets or sets the fixed square atlas side length in pixels, or <see langword="null"/> to use
    /// <see cref="Fonts.FontBakeOptions.DefaultResolution"/>.
    /// </summary>
    public int? Resolution { get; set; }

    /// <summary>
    /// Gets or sets the distance spread in pixels, or <see langword="null"/> to use
    /// <see cref="Fonts.FontBakeOptions.DefaultRange"/>.
    /// </summary>
    public int? Range { get; set; }

    /// <summary>
    /// Gets or sets the extra margin in pixels beyond the range, or <see langword="null"/> to use
    /// <see cref="Fonts.FontBakeOptions.DefaultPadding"/>.
    /// </summary>
    public int? Padding { get; set; }

    /// <summary>
    /// Gets or sets the inclusive codepoint ranges, or <see langword="null"/> to use
    /// <see cref="Fonts.FontBakeOptions.DefaultRangeSelection"/>.
    /// </summary>
    public string[]? Ranges { get; set; }

    /// <summary>
    /// Gets or sets the single codepoints to add to the selection, or <see langword="null"/> for no
    /// additional glyphs.
    /// </summary>
    public string[]? Glyphs { get; set; }
}
