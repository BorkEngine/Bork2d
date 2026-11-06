using System.Globalization;

namespace Bork2d.Bake.Fonts;

/// <summary>
/// Parses a font sidecar's <c>ranges</c> and <c>glyphs</c> keys into the sorted, deduplicated
/// codepoint selection the bake rasterizes.
/// </summary>
/// <remarks>
/// <para>
/// A range is inclusive and uses <c>U+XXXX</c> or <c>U+XXXX-U+YYYY</c> notation; a single glyph is
/// one <c>U+XXXX</c> value. Hexadecimal digits are accepted in either case and leading whitespace
/// around a value is ignored. An individual range may not be inverted, may not name a UTF-16
/// surrogate, and may not exceed <c>U+10FFFF</c>; a malformed value is
/// <see cref="FontDiagnosticCodes.SelectionInvalid"/> rather than a partial bake.
/// </para>
/// <para>
/// The selection combines both keys, deduplicates codepoints, and sorts them ascending. An empty
/// selection is an error, because a font with no glyphs would bake an empty atlas without a useful
/// signal.
/// </para>
/// </remarks>
internal static class FontSelection
{
    private const int MaxCodepoint = 0x10FFFF;
    private const int SurrogateStart = 0xD800;
    private const int SurrogateEnd = 0xDFFF;

    /// <summary>Parses the two selection keys into one sorted codepoint array.</summary>
    /// <param name="ranges">The inclusive <c>U+</c> ranges; already resolved to the sidecar default.</param>
    /// <param name="glyphs">The single <c>U+</c> glyphs; empty when the sidecar has none.</param>
    /// <param name="diagnostics">The diagnostic bag to report an invalid or empty selection to.</param>
    /// <param name="codepoints">The sorted distinct codepoints when successful.</param>
    /// <returns><see langword="true"/> when the selection is valid and non-empty.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static bool TryParse(
        IReadOnlyList<string> ranges,
        IReadOnlyList<string> glyphs,
        FontDiagnostics diagnostics,
        out int[] codepoints)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(diagnostics);

        codepoints = [];
        var selection = new SortedSet<int>();
        bool valid = true;

        foreach (string? range in ranges)
        {
            if (!TryParseRange(range, out int start, out int end, out string? reason))
            {
                diagnostics.Error(
                    FontDiagnosticCodes.SelectionInvalid,
                    $"The font range '{range}' is invalid: {reason}.");
                valid = false;
                continue;
            }

            for (int codepoint = start; codepoint <= end; codepoint++)
            {
                selection.Add(codepoint);
            }
        }

        foreach (string? glyph in glyphs)
        {
            if (!TryParseCodepoint(glyph, out int codepoint, out string? reason))
            {
                diagnostics.Error(
                    FontDiagnosticCodes.SelectionInvalid,
                    $"The font glyph '{glyph}' is invalid: {reason}.");
                valid = false;
                continue;
            }

            selection.Add(codepoint);
        }

        if (!valid)
        {
            return false;
        }

        if (selection.Count == 0)
        {
            diagnostics.Error(
                FontDiagnosticCodes.SelectionInvalid,
                "The font selection is empty; the 'ranges' and 'glyphs' keys must select at least one "
                    + "codepoint.");
            return false;
        }

        codepoints = [.. selection];
        return true;
    }

    private static bool TryParseRange(
        string? text,
        out int start,
        out int end,
        out string? reason)
    {
        start = 0;
        end = 0;
        reason = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            reason = "the value is empty";
            return false;
        }

        string value = text.Trim();
        int separator = value.IndexOf('-');
        string startText = separator < 0 ? value : value[..separator];
        string endText = separator < 0 ? value : value[(separator + 1)..];
        if (!TryParseCodepoint(startText, out start, out reason))
        {
            return false;
        }

        if (!TryParseCodepoint(endText, out end, out reason))
        {
            return false;
        }

        if (start > end)
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"the start U+{start:X4} is greater than the end U+{end:X4}");
            return false;
        }

        return true;
    }

    private static bool TryParseCodepoint(string? text, out int codepoint, out string? reason)
    {
        codepoint = 0;
        reason = null;

        if (string.IsNullOrEmpty(text))
        {
            reason = "the value is empty";
            return false;
        }

        string value = text.Trim();
        if (value.Length < 3
            || (value[0] != 'U' && value[0] != 'u')
            || value[1] != '+')
        {
            reason = "the value must use the U+XXXX or U+XXXX-U+YYYY form";
            return false;
        }

        string digits = value[2..];
        if (digits.Length is < 1 or > 8
            || !int.TryParse(
                digits,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"the codepoint '{value}' is not hexadecimal");
            return false;
        }

        if (parsed is >= SurrogateStart and <= SurrogateEnd)
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"the codepoint 'U+{parsed:X4}' is a UTF-16 surrogate, which is not a Unicode scalar value");
            return false;
        }

        if (parsed < 0 || parsed > MaxCodepoint)
        {
            reason = string.Create(
                CultureInfo.InvariantCulture,
                $"the codepoint 'U+{parsed:X4}' is outside U+0000..U+10FFFF");
            return false;
        }

        codepoint = parsed;
        return true;
    }
}
