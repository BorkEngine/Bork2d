using System.Runtime.CompilerServices;

namespace Bork2d.Engine.Assets;

/// <summary>
/// The immutable render-side copy of a <see cref="FontAsset"/>'s layout data.
/// </summary>
/// <remarks>
/// <para>
/// A font asset is a mutable authoring and loading DTO. Assigning it to a font renderable freezes
/// these fields once, so mutating the asset afterwards — including replacing or editing its glyph
/// and kerning arrays — cannot change a cached layout, a bounds read, or a captured snapshot. The
/// copy holds the atlas reference, the metrics, the distance range, cloned glyph and kerning arrays
/// sorted by codepoint and <c>(LeftCodepoint, RightCodepoint)</c>, and deterministic lookups.
/// </para>
/// <para>
/// Freezing is idempotent per asset instance and allocation-free after the first call: the
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> cache keeps one frozen copy per live asset
/// without a serialized or public cache member, and the weak key lets an unreferenced asset and
/// its copy be collected together. Freezing is thread-safe: concurrent callers for the same asset
/// observe the same published instance.
/// </para>
/// <para>
/// Malformed metadata is tolerated here: null arrays become empty, duplicate codepoints or pairs
/// keep the first entry, and non-finite values are copied as-is. The layout builder later skips
/// malformed glyphs; the freeze path never throws for authored data.
/// </para>
/// </remarks>
internal sealed class FontRenderData
{
    private static readonly ConditionalWeakTable<FontAsset, FontRenderData> _frozen = new();

    private readonly Dictionary<int, FontGlyph> _glyphsByCodepoint;
    private readonly Dictionary<(int Left, int Right), float> _kerningByPair;

    private FontRenderData(FontAsset asset)
    {
        Atlas = asset.Atlas;
        PixelsPerEm = asset.PixelsPerEm;
        Ascent = asset.Ascent;
        Descent = asset.Descent;
        LineGap = asset.LineGap;
        DistanceRange = asset.DistanceRange;
        Glyphs = CloneGlyphs(asset.Glyphs);
        Kerning = CloneKerning(asset.Kerning);
        _glyphsByCodepoint = BuildGlyphLookup(Glyphs);
        _kerningByPair = BuildKerningLookup(Kerning);
    }

    /// <summary>Gets the referenced MSDF atlas descriptor, or an absent reference for no atlas.</summary>
    internal AssetRef<TextureDescriptor> Atlas { get; }

    /// <summary>Gets the frozen derived em size in atlas pixels.</summary>
    internal uint PixelsPerEm { get; }

    /// <summary>Gets the frozen font ascent in em units.</summary>
    internal float Ascent { get; }

    /// <summary>Gets the frozen font descent in em units.</summary>
    internal float Descent { get; }

    /// <summary>Gets the frozen font line gap in em units.</summary>
    internal float LineGap { get; }

    /// <summary>Gets the frozen MSDF distance spread in atlas pixels.</summary>
    internal float DistanceRange { get; }

    /// <summary>Gets the frozen glyphs sorted by codepoint.</summary>
    internal FontGlyph[] Glyphs { get; }

    /// <summary>
    /// Gets the frozen kerning pairs sorted by <c>(LeftCodepoint, RightCodepoint)</c>.
    /// </summary>
    internal FontKerning[] Kerning { get; }

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, creating it on first use.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze.</param>
    /// <returns>The single frozen copy published for this asset instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="asset"/> is <see langword="null"/>.</exception>
    internal static FontRenderData Freeze(FontAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _frozen.GetValue(asset, static key => new FontRenderData(key));
    }

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, or <see langword="null"/> when the
    /// asset is <see langword="null"/>.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze, or <see langword="null"/>.</param>
    /// <returns>The frozen copy, or <see langword="null"/>.</returns>
    internal static FontRenderData? FreezeOrNull(FontAsset? asset)
        => asset is null ? null : Freeze(asset);

    /// <summary>Looks up a frozen glyph by codepoint.</summary>
    /// <param name="codepoint">The Unicode scalar value.</param>
    /// <param name="glyph">The glyph when found; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the frozen asset contains the codepoint.</returns>
    internal bool TryGetGlyph(int codepoint, out FontGlyph glyph)
        => _glyphsByCodepoint.TryGetValue(codepoint, out glyph);

    /// <summary>Looks up a frozen kerning adjustment for an ordered codepoint pair.</summary>
    /// <param name="left">The preceding codepoint.</param>
    /// <param name="right">The following codepoint.</param>
    /// <param name="adjustment">The em-unit adjustment when found; otherwise zero.</param>
    /// <returns><see langword="true"/> when the frozen asset contains the pair.</returns>
    internal bool TryGetKerning(int left, int right, out float adjustment)
    {
        if (_kerningByPair.TryGetValue((left, right), out adjustment))
        {
            return true;
        }

        adjustment = 0f;
        return false;
    }

    private static FontGlyph[] CloneGlyphs(FontGlyph[]? source)
    {
        if (source is null || source.Length == 0)
        {
            return [];
        }

        var unique = new Dictionary<int, FontGlyph>(source.Length);
        foreach (FontGlyph glyph in source)
        {
            // A duplicate codepoint is malformed metadata; keep the first entry in source order.
            _ = unique.TryAdd(glyph.Codepoint, glyph);
        }

        var result = new FontGlyph[unique.Count];
        var index = 0;
        foreach (FontGlyph glyph in unique.Values)
        {
            result[index++] = glyph;
        }

        Array.Sort(result, static (left, right) => left.Codepoint.CompareTo(right.Codepoint));
        return result;
    }

    private static FontKerning[] CloneKerning(FontKerning[]? source)
    {
        if (source is null || source.Length == 0)
        {
            return [];
        }

        var unique = new Dictionary<(int Left, int Right), FontKerning>(source.Length);
        foreach (FontKerning pair in source)
        {
            _ = unique.TryAdd((pair.LeftCodepoint, pair.RightCodepoint), pair);
        }

        var result = new FontKerning[unique.Count];
        var index = 0;
        foreach (FontKerning pair in unique.Values)
        {
            result[index++] = pair;
        }

        Array.Sort(result, static (left, right) =>
        {
            int byLeft = left.LeftCodepoint.CompareTo(right.LeftCodepoint);
            return byLeft != 0 ? byLeft : left.RightCodepoint.CompareTo(right.RightCodepoint);
        });
        return result;
    }

    private static Dictionary<int, FontGlyph> BuildGlyphLookup(FontGlyph[] glyphs)
    {
        var lookup = new Dictionary<int, FontGlyph>(glyphs.Length);
        foreach (FontGlyph glyph in glyphs)
        {
            lookup.Add(glyph.Codepoint, glyph);
        }

        return lookup;
    }

    private static Dictionary<(int Left, int Right), float> BuildKerningLookup(
        FontKerning[] kerning)
    {
        var lookup = new Dictionary<(int Left, int Right), float>(kerning.Length);
        foreach (FontKerning pair in kerning)
        {
            lookup.Add((pair.LeftCodepoint, pair.RightCodepoint), pair.Adjustment);
        }

        return lookup;
    }
}
