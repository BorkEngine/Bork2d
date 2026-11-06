using PolyType;

namespace Bork2d.Engine.Assets;

/// <summary>
/// A MessagePack font asset: the derived em size, the font's vertical metrics, the per-codepoint
/// glyph descriptions, the kerning pairs, and the MSDF atlas that renders them.
/// </summary>
/// <remarks>
/// <para>
/// The atlas is a fixed square texture of <c>Atlas.Width</c> by <c>Atlas.Height</c> pixels in linear
/// <see cref="TextureFormat.Rgb8"/> with a full mip chain. <see cref="PixelsPerEm"/> is the largest
/// integer em size at which every selected glyph still fits the atlas, so quality is predictable for
/// a small character set and an oversized selection fails at bake time instead of degrading silently.
/// </para>
/// <para>
/// <see cref="Ascent"/>, <see cref="Descent"/>, and <see cref="LineGap"/> are the font's vertical
/// metrics divided by its units per em, so ascent is positive and descent is negative.
/// <see cref="DistanceRange"/> is the MSDF distance spread in atlas pixels. The font DTO carries no
/// renderable or layout state; consuming it is the caller's concern.
/// </para>
/// <para>
/// <see cref="Glyphs"/> is sorted by <see cref="FontGlyph.Codepoint"/> and contains only the selected
/// codepoints present in the font. <see cref="Kerning"/> is sorted by
/// <c>(LeftCodepoint, RightCodepoint)</c>. Both arrays are non-null and empty by default, so a loaded
/// asset always exposes a glyph and kerning list.
/// </para>
/// <para>
/// Load with <see cref="IAssets.LoadAsset{T}(string)"/> using the canonical font path. The loader
/// deserializes the packed asset as-is; it does not validate the fields or resolve
/// <see cref="Atlas"/>. Pack creation is responsible for reference integrity, and the bake tooling is
/// responsible for field validation.
/// </para>
/// </remarks>
[GenerateShape]
public sealed partial class FontAsset
{
    /// <summary>
    /// Gets or sets the referenced MSDF atlas texture descriptor, or an absent reference for no atlas.
    /// </summary>
    public AssetRef<TextureDescriptor> Atlas { get; set; }

    /// <summary>
    /// Gets or sets the derived em size in atlas pixels: the largest integer size at which every
    /// selected glyph fits.
    /// </summary>
    public uint PixelsPerEm { get; set; }

    /// <summary>Gets or sets the font ascent in em units; normally positive.</summary>
    public float Ascent { get; set; }

    /// <summary>Gets or sets the font descent in em units; normally negative.</summary>
    public float Descent { get; set; }

    /// <summary>Gets or sets the font line gap in em units; zero when the font declares none.</summary>
    public float LineGap { get; set; }

    /// <summary>
    /// Gets or sets the selected glyphs, sorted by <see cref="FontGlyph.Codepoint"/>.
    /// </summary>
    /// <remarks>
    /// The array is never null after construction, but a malformed loaded asset may contain a null
    /// array; a consumer must tolerate that. Whitespace codepoints are present with a zero plane and
    /// atlas rectangle and their real advance.
    /// </remarks>
    public FontGlyph[] Glyphs { get; set; } = [];

    /// <summary>
    /// Gets or sets the nonzero kerning pairs, sorted by
    /// <c>(LeftCodepoint, RightCodepoint)</c>; empty when the font has no pair data.
    /// </summary>
    public FontKerning[] Kerning { get; set; } = [];

    /// <summary>Gets or sets the MSDF distance spread in atlas pixels.</summary>
    public float DistanceRange { get; set; }
}
