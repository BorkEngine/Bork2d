using System.Numerics;
using System.Text;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Scene;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// The shared local-space text layout used by font renderables and
/// <see cref="Assets.FontAsset.MeasureText(ReadOnlySpan{char}, float)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Layout resolves every Unicode scalar value through the supplied frozen glyphs, substitutes
/// <c>?</c> for a missing codepoint when possible, and skips a codepoint that has no substitute.
/// Skipped codepoints are absent for kerning, so the next effective glyph kerns against the previous
/// effective one. Whitespace glyphs contribute their advance and line width but no quad.
/// </para>
/// <para>
/// The origin is the first line's baseline at <c>y = 0</c>; later lines move down by one line
/// height. The line height is <c>(ascent - descent + lineGap) * size * lineSpacing</c>, and
/// alignment shifts each completed line around <c>x = 0</c>. The helper never throws for malformed
/// glyph, kerning, metric, or text data; it returns <see cref="FontLayoutData.Empty"/> when the
/// inputs cannot produce a finite layout.
/// </para>
/// </remarks>
internal static class FontLayoutBuilder
{
    private const int CarriageReturn = '\r';
    private const int LineFeed = '\n';
    private const int MissingGlyphCodepoint = '?';

    /// <summary>
    /// Computes the immutable local layout for <paramref name="text"/>.
    /// </summary>
    /// <param name="glyphs">The frozen glyphs, or <see langword="null"/> for none.</param>
    /// <param name="kerning">The frozen kerning pairs, or <see langword="null"/> for none.</param>
    /// <param name="ascent">The font ascent in em units.</param>
    /// <param name="descent">The font descent in em units; normally negative.</param>
    /// <param name="lineGap">The font line gap in em units.</param>
    /// <param name="text">The UTF-16 text to lay out.</param>
    /// <param name="size">The world units per em.</param>
    /// <param name="alignment">The per-line horizontal alignment.</param>
    /// <param name="lineSpacing">The default-line-height multiplier.</param>
    /// <param name="missingGlyph">
    /// Called once for each skipped codepoint, or <see langword="null"/> for no notification.
    /// </param>
    /// <returns>The local layout, or <see cref="FontLayoutData.Empty"/> for empty or invalid input.</returns>
    internal static FontLayoutData Build(
        FontGlyph[]? glyphs,
        FontKerning[]? kerning,
        float ascent,
        float descent,
        float lineGap,
        ReadOnlySpan<char> text,
        float size,
        TextAlignment alignment,
        float lineSpacing,
        Action<int>? missingGlyph)
    {
        if (text.IsEmpty
            || !TryGetLineHeight(ascent, descent, lineGap, size, lineSpacing, out float lineHeight))
        {
            return FontLayoutData.Empty;
        }

        Dictionary<int, FontGlyph> glyphLookup = BuildGlyphLookup(glyphs);
        Dictionary<(int Left, int Right), float> kerningLookup = BuildKerningLookup(kerning);
        var quads = new List<FontGlyphQuad>();
        var line = new List<FontGlyph>();
        var maxWidth = 0f;
        var lineIndex = 0;
        var skipLineFeed = false;

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (skipLineFeed)
            {
                skipLineFeed = false;
                if (rune.Value == LineFeed)
                {
                    continue;
                }
            }

            if (rune.Value == CarriageReturn || rune.Value == LineFeed)
            {
                if (!LayoutLine(
                    line,
                    quads,
                    kerningLookup,
                    alignment,
                    -lineIndex * lineHeight,
                    size,
                    ref maxWidth))
                {
                    return FontLayoutData.Empty;
                }

                line.Clear();
                lineIndex++;
                skipLineFeed = rune.Value == CarriageReturn;
                continue;
            }

            if (TryGetEffectiveGlyph(glyphLookup, rune.Value, missingGlyph, out FontGlyph glyph))
            {
                line.Add(glyph);
            }
        }

        if (!LayoutLine(
            line,
            quads,
            kerningLookup,
            alignment,
            -lineIndex * lineHeight,
            size,
            ref maxWidth))
        {
            return FontLayoutData.Empty;
        }

        lineIndex++;
        float height = lineIndex * lineHeight;
        if (!float.IsFinite(height) || height <= 0f)
        {
            return FontLayoutData.Empty;
        }

        Bounds localBounds = BuildLocalBounds(quads);
        FontGlyphQuad[] glyphQuads = quads.Count == 0 ? [] : [.. quads];
        return new FontLayoutData(glyphQuads, localBounds, maxWidth, height);
    }

    /// <summary>
    /// Computes the default line height for a size and spacing multiplier.
    /// </summary>
    /// <param name="ascent">The font ascent in em units.</param>
    /// <param name="descent">The font descent in em units.</param>
    /// <param name="lineGap">The font line gap in em units.</param>
    /// <param name="size">The world units per em.</param>
    /// <param name="lineSpacing">The default-line-height multiplier.</param>
    /// <param name="lineHeight">The positive finite line height when the inputs are valid.</param>
    /// <returns><see langword="true"/> when every metric and multiplier is valid.</returns>
    internal static bool TryGetLineHeight(
        float ascent,
        float descent,
        float lineGap,
        float size,
        float lineSpacing,
        out float lineHeight)
    {
        lineHeight = 0f;
        if (!float.IsFinite(ascent)
            || !float.IsFinite(descent)
            || !float.IsFinite(lineGap)
            || !float.IsFinite(size)
            || size <= 0f
            || !float.IsFinite(lineSpacing)
            || lineSpacing <= 0f)
        {
            return false;
        }

        float emHeight = ascent - descent + lineGap;
        if (!float.IsFinite(emHeight) || emHeight <= 0f)
        {
            return false;
        }

        lineHeight = emHeight * size * lineSpacing;
        return float.IsFinite(lineHeight) && lineHeight > 0f;
    }

    private static Dictionary<int, FontGlyph> BuildGlyphLookup(FontGlyph[]? glyphs)
    {
        var lookup = new Dictionary<int, FontGlyph>();
        if (glyphs is null)
        {
            return lookup;
        }

        foreach (FontGlyph glyph in glyphs)
        {
            // A duplicate codepoint is malformed metadata; the first entry wins, matching the
            // frozen render-data copy.
            _ = lookup.TryAdd(glyph.Codepoint, glyph);
        }

        return lookup;
    }

    private static Dictionary<(int Left, int Right), float> BuildKerningLookup(
        FontKerning[]? kerning)
    {
        var lookup = new Dictionary<(int Left, int Right), float>();
        if (kerning is null)
        {
            return lookup;
        }

        foreach (FontKerning pair in kerning)
        {
            _ = lookup.TryAdd(
                (pair.LeftCodepoint, pair.RightCodepoint),
                pair.Adjustment);
        }

        return lookup;
    }

    private static bool TryGetEffectiveGlyph(
        Dictionary<int, FontGlyph> glyphs,
        int codepoint,
        Action<int>? missingGlyph,
        out FontGlyph glyph)
    {
        if (glyphs.TryGetValue(codepoint, out glyph))
        {
            if (IsGlyphUsable(glyph))
            {
                return true;
            }

            glyph = default;
            return false;
        }

        if (codepoint != MissingGlyphCodepoint
            && glyphs.TryGetValue(MissingGlyphCodepoint, out FontGlyph substitute)
            && IsGlyphUsable(substitute))
        {
            glyph = substitute;
            return true;
        }

        missingGlyph?.Invoke(codepoint);
        glyph = default;
        return false;
    }

    private static bool IsGlyphUsable(FontGlyph glyph)
    {
        if (!float.IsFinite(glyph.Advance)
            || !IsFinite(glyph.PlaneBounds)
            || !IsFinite(glyph.AtlasBounds))
        {
            return false;
        }

        // A whitespace glyph keeps its advance with a zero plane and atlas rectangle. Any other
        // glyph needs a positive plane quad; skip a malformed zero-area cell without throwing.
        if (glyph.PlaneBounds == Vector4.Zero && glyph.AtlasBounds == Vector4.Zero)
        {
            return true;
        }

        return glyph.PlaneBounds.Z > glyph.PlaneBounds.X
            && glyph.PlaneBounds.W > glyph.PlaneBounds.Y;
    }

    private static bool LayoutLine(
        List<FontGlyph> glyphs,
        List<FontGlyphQuad> quads,
        Dictionary<(int Left, int Right), float> kerning,
        TextAlignment alignment,
        float baselineY,
        float size,
        ref float maxWidth)
    {
        if (!float.IsFinite(baselineY))
        {
            return false;
        }

        float width = 0f;
        for (var index = 0; index < glyphs.Count; index++)
        {
            if (index > 0)
            {
                width += KerningAdjustment(
                    kerning,
                    glyphs[index - 1].Codepoint,
                    glyphs[index].Codepoint) * size;
            }

            width += glyphs[index].Advance * size;
            if (!float.IsFinite(width))
            {
                return false;
            }
        }

        float alignmentOffset = alignment switch
        {
            TextAlignment.Center => -width * 0.5f,
            TextAlignment.Right => -width,
            _ => 0f,
        };

        float penX = alignmentOffset;
        for (var index = 0; index < glyphs.Count; index++)
        {
            FontGlyph glyph = glyphs[index];
            if (index > 0)
            {
                penX += KerningAdjustment(
                    kerning,
                    glyphs[index - 1].Codepoint,
                    glyph.Codepoint) * size;
            }

            Vector4 plane = glyph.PlaneBounds;
            float x = penX + (plane.X * size);
            float y = baselineY + (plane.Y * size);
            float quadWidth = (plane.Z - plane.X) * size;
            float quadHeight = (plane.W - plane.Y) * size;
            if (quadWidth > 0f
                && quadHeight > 0f
                && float.IsFinite(x)
                && float.IsFinite(y)
                && float.IsFinite(quadWidth)
                && float.IsFinite(quadHeight))
            {
                quads.Add(new FontGlyphQuad(
                    new Vector2(x, y),
                    new Vector2(quadWidth, quadHeight),
                    glyph.AtlasBounds));
            }

            penX += glyph.Advance * size;
            if (!float.IsFinite(penX))
            {
                return false;
            }
        }

        maxWidth = MathF.Max(maxWidth, width);
        return true;
    }

    private static float KerningAdjustment(
        Dictionary<(int Left, int Right), float> kerning,
        int left,
        int right)
        => kerning.TryGetValue((left, right), out float adjustment)
            && float.IsFinite(adjustment)
            ? adjustment
            : 0f;

    private static Bounds BuildLocalBounds(List<FontGlyphQuad> quads)
    {
        if (quads.Count == 0)
        {
            return Bounds.Empty;
        }

        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (FontGlyphQuad quad in quads)
        {
            Vector2 quadMax = quad.Min + quad.Size;
            if (!IsFinite(quad.Min) || !IsFinite(quadMax))
            {
                return Bounds.Empty;
            }

            min = Vector2.Min(min, quad.Min);
            max = Vector2.Max(max, quadMax);
        }

        Bounds bounds = Bounds.FromMinMax(min, max);
        return bounds.IsEmpty ? Bounds.Empty : bounds;
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z)
            && float.IsFinite(value.W);
}
