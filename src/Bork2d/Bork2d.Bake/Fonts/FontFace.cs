using System.Runtime.InteropServices;
using Season.MSDF;
using StbTrueTypeSharp;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// One initialized TrueType/CFF font: its vertical metrics, glyph lookup, and outline reading.
/// </summary>
/// <remarks>
/// <para>
/// The font bytes are pinned for the lifetime of the instance and initialized with
/// <c>stbtt_GetFontOffsetForIndex</c> (index zero) followed by <c>stbtt_InitFont</c>. A recognized
/// font whose tables initialize owns the pin until <see cref="Dispose"/>; a reader failure releases
/// the pin and reports <see cref="FontDiagnosticCodes.SourceUnreadable"/>.
/// </para>
/// <para>
/// The instance is not thread-safe; one instance belongs to one bake on the bake thread. Shapes are
/// built in em units by <see cref="FontOutlineBuilder"/> and freed back to the native library through
/// <c>stbtt_FreeShape</c> after the vertices have been copied into the managed shape.
/// </para>
/// </remarks>
internal sealed unsafe class FontFace : IDisposable
{
    private readonly GCHandle _dataHandle;
    private readonly StbTrueType.stbtt_fontinfo _info;
    private bool _disposed;

    private FontFace(
        GCHandle dataHandle,
        StbTrueType.stbtt_fontinfo info,
        int unitsPerEm,
        float ascent,
        float descent,
        float lineGap)
    {
        _dataHandle = dataHandle;
        _info = info;
        UnitsPerEm = unitsPerEm;
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
    }

    /// <summary>Gets the font's units per em, the divisor for em-unit measurements.</summary>
    internal int UnitsPerEm { get; }

    /// <summary>Gets the font ascent in em units; normally positive.</summary>
    internal float Ascent { get; }

    /// <summary>Gets the font descent in em units; normally negative.</summary>
    internal float Descent { get; }

    /// <summary>Gets the font line gap in em units; zero when the font declares none.</summary>
    internal float LineGap { get; }

    /// <summary>Reads a font source and initializes its outline tables.</summary>
    /// <param name="source">The complete TTF/OTF file bytes.</param>
    /// <param name="diagnostics">The diagnostic bag to report an unreadable source to.</param>
    /// <returns>The initialized font, or <see langword="null"/> after reporting <c>BK9301</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static FontFace? TryCreate(byte[] source, FontDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (source.Length == 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SourceUnreadable,
                "The font source is empty.");
            return null;
        }

        GCHandle handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        bool created = false;
        try
        {
            byte* pointer = (byte*)handle.AddrOfPinnedObject();
            int offset = StbTrueType.stbtt_GetFontOffsetForIndex(pointer, 0);
            if (offset < 0)
            {
                diagnostics.Error(
                    FontDiagnosticCodes.SourceUnreadable,
                    "The font source is not a recognized TTF/OTF container: no font table was found.");
                return null;
            }

            var info = new StbTrueType.stbtt_fontinfo();
            if (StbTrueType.stbtt_InitFont(info, pointer, offset) == 0)
            {
                diagnostics.Error(
                    FontDiagnosticCodes.SourceUnreadable,
                    "The font source is not a recognized TTF/OTF container: its outline tables could "
                        + "not be initialized.");
                return null;
            }

            int unitsPerEm = StbTrueType.ttUSHORT(pointer + info.head + 18);
            if (unitsPerEm <= 0)
            {
                diagnostics.Error(
                    FontDiagnosticCodes.SourceUnreadable,
                    "The font source declares a non-positive units-per-em value.");
                return null;
            }

            int ascent;
            int descent;
            int lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
            float scale = unitsPerEm;
            var face = new FontFace(
                handle,
                info,
                unitsPerEm,
                ascent / scale,
                descent / scale,
                lineGap / scale);
            created = true;
            return face;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SourceUnreadable,
                $"The font source is not a readable TTF/OTF font: {exception.Message}");
            return null;
        }
        finally
        {
            if (!created)
            {
                handle.Free();
            }
        }
    }

    /// <summary>Finds the glyph index a Unicode codepoint maps to.</summary>
    /// <param name="codepoint">The Unicode scalar value.</param>
    /// <returns>The glyph index, or <c>0</c> when the font has no glyph for the codepoint.</returns>
    internal int FindGlyphIndex(int codepoint)
        => StbTrueType.stbtt_FindGlyphIndex(_info, codepoint);

    /// <summary>Reads the pair kerning adjustment between two glyph indexes.</summary>
    /// <param name="leftGlyphIndex">The preceding glyph index.</param>
    /// <param name="rightGlyphIndex">The following glyph index.</param>
    /// <returns>
    /// The adjustment in font units, or <c>0</c> when the font has no kern/GPOS data for the pair.
    /// </returns>
    internal int GetKerning(int leftGlyphIndex, int rightGlyphIndex)
        => StbTrueType.stbtt_GetGlyphKernAdvance(_info, leftGlyphIndex, rightGlyphIndex);

    /// <summary>Reads one glyph's advance and outline.</summary>
    /// <param name="glyphIndex">The glyph index; nonzero.</param>
    /// <param name="advance">The advance in font units when successful.</param>
    /// <param name="shape">
    /// The glyph outline in em units, or <see langword="null"/> when the glyph is empty (for example a
    /// space) and has no drawable outline.
    /// </param>
    /// <param name="failure">The reason the outline could not be built, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the glyph was read or is empty.</returns>
    internal bool TryGetGlyph(
        int glyphIndex,
        out int advance,
        out Shape? shape,
        out string? failure)
    {
        advance = 0;
        shape = null;
        failure = null;

        StbTrueType.stbtt_vertex* vertices = null;
        try
        {
            int advanceWidth;
            int leftSideBearing;
            StbTrueType.stbtt_GetGlyphHMetrics(
                _info,
                glyphIndex,
                &advanceWidth,
                &leftSideBearing);
            advance = advanceWidth;

            if (StbTrueType.stbtt_IsGlyphEmpty(_info, glyphIndex) != 0)
            {
                return true;
            }

            int count = StbTrueType.stbtt_GetGlyphShape(_info, glyphIndex, &vertices);
            if (count <= 0)
            {
                return true;
            }

            var span = new ReadOnlySpan<StbTrueType.stbtt_vertex>(vertices, count);
            return FontOutlineBuilder.TryBuild(span, UnitsPerEm, out shape, out failure);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = $"the outline reader failed: {exception.Message}";
            return false;
        }
        finally
        {
            if (vertices is not null)
            {
                StbTrueType.stbtt_FreeShape(_info, vertices);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _info.Dispose();
        _dataHandle.Free();
    }
}
