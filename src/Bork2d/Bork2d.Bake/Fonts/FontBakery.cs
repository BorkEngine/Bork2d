using System.Globalization;
using System.Numerics;
using Bork2d.Engine.Assets;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// Bakes a selected set of codepoints from one TTF/OTF source into a fixed square MSDF atlas and a
/// <see cref="FontAsset"/>.
/// </summary>
/// <remarks>
/// <para>
/// The atlas is exactly <c>resolution × resolution</c> pixels: it is never cropped, so the derived em
/// size is the only quality knob. The baker groups selected codepoints by glyph index, builds each
/// unique outline in em units, and binary-searches the largest integer em size in
/// <c>[8, resolution]</c> whose deterministic shelf layout fits the atlas. A selection that does not
/// fit at the minimum em size is <see cref="FontDiagnosticCodes.AtlasDoesNotFit"/>.
/// </para>
/// <para>
/// A selection whose codepoints all map to whitespace glyphs is valid and emits an all-outside atlas
/// at the deterministic em size <c>8</c>; the glyph entries keep their real advances and zero plane
/// and atlas rectangles. A selection with no glyph mapping at all is
/// <see cref="FontDiagnosticCodes.AtlasDoesNotFit"/>.
/// </para>
/// <para>
/// Every non-empty glyph field must contain inside and outside texels and must agree with the exact
/// outline distance on a deterministic sample grid; a failure is
/// <see cref="FontDiagnosticCodes.FieldInvalid"/>. The atlas is written as linear
/// <see cref="TextureFormat.Rgb8"/> with a full mip chain, and a payload failure is
/// <see cref="FontDiagnosticCodes.PayloadWriteFailed"/>. On any error the baker returns
/// <see langword="null"/> and the processor emits no outputs.
/// </para>
/// </remarks>
internal static class FontBakery
{
    /// <summary>Bakes one font source into a font asset and its atlas payload.</summary>
    /// <param name="source">The complete TTF/OTF file bytes.</param>
    /// <param name="options">The authored resolution, range, and padding.</param>
    /// <param name="ranges">The inclusive <c>U+</c> ranges; already resolved to the sidecar default.</param>
    /// <param name="glyphs">The single <c>U+</c> glyphs; empty when the sidecar has none.</param>
    /// <param name="atlasAssetPath">The canonical atlas asset path the font references.</param>
    /// <param name="diagnostics">The diagnostic bag to report a failure to.</param>
    /// <returns>The baked font and atlas, or <see langword="null"/> after reporting a diagnostic.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static FontBakeResult? Bake(
        byte[] source,
        FontBakeOptions options,
        IReadOnlyList<string> ranges,
        IReadOnlyList<string> glyphs,
        string atlasAssetPath,
        FontDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasAssetPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (options.Resolution <= 0 || options.Resolution > FontBakeOptions.MaxResolution)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SelectionInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The font atlas resolution {options.Resolution} is outside "
                        + $"1..{FontBakeOptions.MaxResolution}."));
            return null;
        }

        if (options.Range <= 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SelectionInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The font range {options.Range} must be positive."));
            return null;
        }

        if (options.Padding < 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SelectionInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The font padding {options.Padding} must not be negative."));
            return null;
        }

        long border = (long)options.Range + options.Padding;
        if (border <= 0 || (2 * border) >= options.Resolution)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SelectionInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The font range+padding margin is {border} pixels on every side, which leaves no "
                        + $"usable {options.Resolution}x{options.Resolution} atlas."));
            return null;
        }

        if (!FontSelection.TryParse(ranges, glyphs, diagnostics, out int[] codepoints))
        {
            return null;
        }

        using FontFace? face = FontFace.TryCreate(source, diagnostics);
        if (face is null)
        {
            return null;
        }

        var glyphByCodepoint = new Dictionary<int, int>(codepoints.Length);
        foreach (int codepoint in codepoints)
        {
            int glyphIndex = face.FindGlyphIndex(codepoint);
            if (glyphIndex == 0)
            {
                diagnostics.Info(
                    FontDiagnosticCodes.GlyphMissing,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The codepoint U+{codepoint:X4} is not present in the font; it was skipped."));
                continue;
            }

            glyphByCodepoint[codepoint] = glyphIndex;
        }

        if (glyphByCodepoint.Count == 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.AtlasDoesNotFit,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The font selection has no usable glyphs (0 of {codepoints.Length} selected "
                        + $"codepoints) for a {options.Resolution}x{options.Resolution} atlas."));
            return null;
        }

        if (!TryReadGlyphs(
            face,
            codepoints,
            glyphByCodepoint,
            diagnostics,
            out Dictionary<int, GlyphInfo> glyphInfos))
        {
            return null;
        }

        int borderPixels = (int)border;
        int em;
        Dictionary<int, FontAtlasPacker.Placement> placements;
        var drawable = new List<GlyphInfo>(glyphInfos.Count);
        foreach (GlyphInfo glyphInfo in glyphInfos.Values)
        {
            if (glyphInfo.Shape is not null)
            {
                drawable.Add(glyphInfo);
            }
        }

        if (drawable.Count == 0)
        {
            // A whitespace-only selection has no cells to pack; keep a deterministic em size so the
            // asset is reproducible and the atlas is a valid all-outside field.
            em = FontBakeOptions.MinPixelsPerEm;
            placements = [];
        }
        else if (!TryDeriveEm(options, borderPixels, drawable, out em, out placements))
        {
            diagnostics.Error(
                FontDiagnosticCodes.AtlasDoesNotFit,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {drawable.Count} drawable glyphs from {glyphByCodepoint.Count} selected "
                        + $"codepoints do not fit a {options.Resolution}x{options.Resolution} atlas "
                        + $"at the minimum {FontBakeOptions.MinPixelsPerEm}-pixel em size."));
            return null;
        }

        byte[] atlas = CreateAtlas(options.Resolution);
        foreach (GlyphInfo glyphInfo in drawable)
        {
            FontAtlasPacker.Placement placement = placements[glyphInfo.GlyphIndex];
            FontMsdfCell? cell = FontMsdfCell.Create(
                glyphInfo.Shape!,
                em,
                borderPixels,
                options.Range,
                placement.Width,
                placement.Height,
                DescribeGlyph(glyphInfo.Codepoints[0], glyphInfo.GlyphIndex),
                diagnostics);
            if (cell is null)
            {
                return null;
            }

            CopyCell(atlas, options.Resolution, cell, placement);
        }

        var entries = new List<FontGlyph>(glyphByCodepoint.Count);
        foreach (int codepoint in codepoints)
        {
            if (!glyphByCodepoint.TryGetValue(codepoint, out int glyphIndex))
            {
                continue;
            }

            GlyphInfo glyphInfo = glyphInfos[glyphIndex];
            float advance = glyphInfo.Advance / (float)face.UnitsPerEm;
            if (glyphInfo.Shape is null)
            {
                entries.Add(new FontGlyph(
                    codepoint,
                    advance,
                    Vector2.Zero,
                    Vector4.Zero,
                    Vector4.Zero));
                continue;
            }

            FontAtlasPacker.Placement placement = placements[glyphIndex];
            Season.MSDF.Shape.Bounds bounds = glyphInfo.Shape.GetBounds(0, 1, 0);
            double left = bounds.L - ((double)borderPixels / em);
            double bottom = bounds.B - ((double)borderPixels / em);
            var planeBounds = new Vector4(
                (float)left,
                (float)bottom,
                (float)(left + ((double)placement.Width / em)),
                (float)(bottom + ((double)placement.Height / em)));
            var atlasBounds = new Vector4(
                placement.X / (float)options.Resolution,
                placement.Y / (float)options.Resolution,
                (placement.X + placement.Width) / (float)options.Resolution,
                (placement.Y + placement.Height) / (float)options.Resolution);
            entries.Add(new FontGlyph(
                codepoint,
                advance,
                new Vector2(planeBounds.X, planeBounds.Y),
                planeBounds,
                atlasBounds));
        }

        FontKerning[] kerning = BuildKerning(codepoints, glyphByCodepoint, face.UnitsPerEm, face.GetKerning);

        BakedTexture texture;
        try
        {
            texture = TextureBaker.Bake(
                atlas,
                options.Resolution,
                options.Resolution,
                TextureChannelType.Rgb,
                TextureCompression.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Error(
                FontDiagnosticCodes.PayloadWriteFailed,
                $"The font atlas payload could not be encoded: {exception.Message}");
            return null;
        }

        var font = new FontAsset
        {
            Atlas = atlasAssetPath,
            PixelsPerEm = (uint)em,
            Ascent = face.Ascent,
            Descent = face.Descent,
            LineGap = face.LineGap,
            Glyphs = [.. entries],
            Kerning = kerning,
            DistanceRange = options.Range,
        };

        return new FontBakeResult(font, texture, options.Resolution);
    }

    private static bool TryReadGlyphs(
        FontFace face,
        int[] codepoints,
        Dictionary<int, int> glyphByCodepoint,
        FontDiagnostics diagnostics,
        out Dictionary<int, GlyphInfo> glyphInfos)
    {
        glyphInfos = [];

        foreach (int codepoint in codepoints)
        {
            if (!glyphByCodepoint.TryGetValue(codepoint, out int glyphIndex))
            {
                continue;
            }

            if (glyphInfos.TryGetValue(glyphIndex, out GlyphInfo? existing))
            {
                existing.Codepoints.Add(codepoint);
                continue;
            }

            if (!face.TryGetGlyph(
                glyphIndex,
                out int advance,
                out Season.MSDF.Shape? shape,
                out string? failure))
            {
                diagnostics.Error(
                    FontDiagnosticCodes.FieldInvalid,
                    $"The glyph {DescribeGlyph(codepoint, glyphIndex)} could not be read: {failure}.");
                return false;
            }

            var glyphInfo = new GlyphInfo(glyphIndex, advance, shape);
            glyphInfo.Codepoints.Add(codepoint);
            glyphInfos.Add(glyphIndex, glyphInfo);
        }

        return true;
    }

    private static bool TryDeriveEm(
        FontBakeOptions options,
        int border,
        List<GlyphInfo> glyphs,
        out int em,
        out Dictionary<int, FontAtlasPacker.Placement> placements)
    {
        int low = FontBakeOptions.MinPixelsPerEm;
        int high = options.Resolution;
        em = 0;
        placements = [];

        while (low <= high)
        {
            int candidate = low + ((high - low) / 2);
            if (TryPack(candidate, options.Resolution, border, glyphs, out Dictionary<int, FontAtlasPacker.Placement> candidatePlacements))
            {
                em = candidate;
                placements = candidatePlacements;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }

        return em >= FontBakeOptions.MinPixelsPerEm;
    }

    private static bool TryPack(
        int em,
        int resolution,
        int border,
        List<GlyphInfo> glyphs,
        out Dictionary<int, FontAtlasPacker.Placement> placements)
    {
        var cells = new List<FontAtlasPacker.Cell>(glyphs.Count);
        foreach (GlyphInfo glyph in glyphs)
        {
            Season.MSDF.Shape.Bounds bounds = glyph.Shape!.GetBounds(0, 1, 0);
            double width = Math.Ceiling((bounds.R - bounds.L) * em) + (2.0 * border);
            double height = Math.Ceiling((bounds.T - bounds.B) * em) + (2.0 * border);
            if (!double.IsFinite(width) || !double.IsFinite(height)
                || width > int.MaxValue || height > int.MaxValue)
            {
                placements = [];
                return false;
            }

            cells.Add(new FontAtlasPacker.Cell(
                glyph.GlyphIndex,
                glyph.Codepoints[0],
                (int)width,
                (int)height));
        }

        return FontAtlasPacker.TryPack(cells, resolution, out placements);
    }

    private static byte[] CreateAtlas(int resolution)
    {
        var atlas = new byte[resolution * resolution * 4];
        for (int alpha = 3; alpha < atlas.Length; alpha += 4)
        {
            atlas[alpha] = 255;
        }

        return atlas;
    }

    private static void CopyCell(
        byte[] atlas,
        int resolution,
        FontMsdfCell cell,
        FontAtlasPacker.Placement placement)
    {
        for (int y = 0; y < cell.Height; y++)
        {
            int sourceOffset = y * cell.Width * 4;
            int destinationOffset = (((placement.Y + y) * resolution) + placement.X) * 4;
            cell.Pixels.AsSpan(sourceOffset, cell.Width * 4).CopyTo(atlas.AsSpan(destinationOffset));
        }
    }

    /// <summary>Builds the sorted nonzero kerning pairs of a selected codepoint set.</summary>
    /// <param name="codepoints">The selected codepoints, sorted ascending.</param>
    /// <param name="glyphByCodepoint">The selected codepoints that map to a glyph.</param>
    /// <param name="unitsPerEm">The font's units per em, the divisor for the adjustment.</param>
    /// <param name="kerning">
    /// Returns the adjustment in font units for a left/right glyph pair; a return value of zero means
    /// the pair has no kerning.
    /// </param>
    /// <returns>The kerning entries sorted by <c>(LeftCodepoint, RightCodepoint)</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unitsPerEm"/> is not positive.</exception>
    /// <remarks>
    /// The pair loop walks both codepoint lists in ordinal order, so the result is deterministic. A
    /// font with malformed pair data contributes no adjustment instead of failing the whole atlas.
    /// </remarks>
    internal static FontKerning[] BuildKerning(
        int[] codepoints,
        Dictionary<int, int> glyphByCodepoint,
        int unitsPerEm,
        Func<int, int, int> kerning)
    {
        ArgumentNullException.ThrowIfNull(codepoints);
        ArgumentNullException.ThrowIfNull(glyphByCodepoint);
        ArgumentNullException.ThrowIfNull(kerning);
        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerEm),
                unitsPerEm,
                "The units-per-em value must be positive.");
        }

        var entries = new List<FontKerning>();
        foreach (int leftCodepoint in codepoints)
        {
            if (!glyphByCodepoint.TryGetValue(leftCodepoint, out int leftGlyph))
            {
                continue;
            }

            foreach (int rightCodepoint in codepoints)
            {
                if (!glyphByCodepoint.TryGetValue(rightCodepoint, out int rightGlyph))
                {
                    continue;
                }

                int adjustment;
                try
                {
                    adjustment = kerning(leftGlyph, rightGlyph);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Kerning is optional metadata; a font with malformed pair data still bakes its
                    // atlas, and the pair contributes no adjustment.
                    adjustment = 0;
                }

                if (adjustment == 0)
                {
                    continue;
                }

                entries.Add(new FontKerning(
                    leftCodepoint,
                    rightCodepoint,
                    adjustment / (float)unitsPerEm));
            }
        }

        return [.. entries];
    }

    private static string DescribeGlyph(int codepoint, int glyphIndex)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"codepoint U+{codepoint:X4} (glyph {glyphIndex})");

    private sealed class GlyphInfo
    {
        internal GlyphInfo(int glyphIndex, int advance, Season.MSDF.Shape? shape)
        {
            GlyphIndex = glyphIndex;
            Advance = advance;
            Shape = shape;
        }

        internal int GlyphIndex { get; }

        internal int Advance { get; }

        internal Season.MSDF.Shape? Shape { get; }

        internal List<int> Codepoints { get; } = [];
    }
}
