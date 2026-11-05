using System.Globalization;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Reads SVG numbers and lengths from text without splitting it into tokens.
/// </summary>
/// <remarks>
/// The scanner follows the SVG number grammar: an optional sign, digits with an optional decimal
/// point, and an optional exponent. A caller supplies separators: whitespace may surround at most one
/// comma between two numbers, and a sign may start the next number directly (so <c>10-20</c> is two
/// numbers). Units are only accepted where <see cref="TryParseLength"/> is used, and only a plain
/// number or the <c>px</c> suffix is valid there.
/// </remarks>
internal static class SvgNumberParser
{
    /// <summary>Gets a value indicating whether the character can start a number.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> for a sign, a decimal point, or a digit.</returns>
    internal static bool IsNumberStart(char character)
        => character is '+' or '-' or '.' || char.IsAsciiDigit(character);

    /// <summary>Gets a value indicating whether the character is SVG whitespace.</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> for a space, tab, carriage return, line feed, or form feed.</returns>
    internal static bool IsWhitespace(char character)
        => character is ' ' or '\t' or '\r' or '\n' or '\f';

    /// <summary>Skips whitespace and at most one comma.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="position">The current position; advanced past the skipped separators.</param>
    internal static void SkipSeparators(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length && IsWhitespace(text[position]))
        {
            position++;
        }

        if (position < text.Length && text[position] == ',')
        {
            position++;
            while (position < text.Length && IsWhitespace(text[position]))
            {
                position++;
            }
        }
    }

    /// <summary>Reads one SVG number starting at the current position.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="position">The current position; advanced past the parsed number on success.</param>
    /// <param name="value">The parsed value; it may be infinite for an overflowing exponent.</param>
    /// <returns><see langword="false"/> when no syntactically valid number starts at the position.</returns>
    internal static bool TryRead(ReadOnlySpan<char> text, ref int position, out double value)
    {
        value = 0;

        int start = position;
        int index = position;
        if (index < text.Length && (text[index] == '+' || text[index] == '-'))
        {
            index++;
        }

        int digits = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
            digits++;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            int exponentStart = index;
            index++;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                index++;
            }

            int exponentDigits = 0;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
                exponentDigits++;
            }

            // "1e" is the number 1 followed by an unexpected character, not a broken exponent.
            if (exponentDigits == 0)
            {
                index = exponentStart;
            }
        }

        if (!double.TryParse(
                text.Slice(start, index - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        position = index;
        return true;
    }

    /// <summary>Parses a whole SVG length: a finite plain number or a finite number with a <c>px</c> suffix.</summary>
    /// <param name="text">The attribute value, or <see langword="null"/> when the attribute is absent.</param>
    /// <param name="value">The parsed user-unit value on success.</param>
    /// <returns>
    /// <see langword="false"/> for <see langword="null"/>, empty, non-finite, or any other unit
    /// (percentages, <c>em</c>, <c>ex</c>, and so on).
    /// </returns>
    internal static bool TryParseLength(string? text, out double value)
    {
        value = 0;
        if (text is null)
        {
            return false;
        }

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.IsEmpty)
        {
            return false;
        }

        int position = 0;
        if (!TryRead(span, ref position, out value) || !double.IsFinite(value))
        {
            return false;
        }

        ReadOnlySpan<char> suffix = span[position..];
        return suffix.IsEmpty || suffix.SequenceEqual("px");
    }
}
