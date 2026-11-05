namespace Bork2d.Bake.Svg;

/// <summary>
/// Parses an SVG <c>transform</c> attribute into one composed affine transform.
/// </summary>
/// <remarks>
/// <para>
/// The supported functions are <c>matrix</c>, <c>translate</c>, <c>scale</c>, <c>rotate</c>,
/// <c>skewX</c>, and <c>skewY</c>. A list composes left to right as SVG specifies: the rightmost
/// function applies to a point first, so the parser accumulates <c>result * next</c>. Function names
/// are case-sensitive, as in SVG.
/// </para>
/// <para>
/// The parser never throws for malformed data. Angles are degrees; argument counts are exact; an
/// absent or empty attribute is the identity.
/// </para>
/// </remarks>
internal static class SvgTransformParser
{
    /// <summary>Parses a transform attribute value.</summary>
    /// <param name="text">The attribute value; <see langword="null"/> or blank means identity.</param>
    /// <param name="transform">The composed transform, or identity on failure.</param>
    /// <param name="message">The failure message, or an empty string on success.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    internal static bool TryParse(string? text, out SvgAffine transform, out string message)
    {
        transform = SvgAffine.Identity;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        ReadOnlySpan<char> span = text.AsSpan();
        int position = 0;
        while (true)
        {
            SvgNumberParser.SkipSeparators(span, ref position);
            if (position >= span.Length)
            {
                break;
            }

            int nameStart = position;
            while (position < span.Length && char.IsAsciiLetter(span[position]))
            {
                position++;
            }

            string name = span[nameStart..position].ToString();
            if (name.Length == 0)
            {
                message = $"expected a transform function at character offset {nameStart}";
                return false;
            }

            SvgNumberParser.SkipSeparators(span, ref position);
            if (position >= span.Length || span[position] != '(')
            {
                message = $"expected '(' after the transform function '{name}'";
                return false;
            }

            position++;
            var arguments = new List<double>();
            while (true)
            {
                SvgNumberParser.SkipSeparators(span, ref position);
                if (position >= span.Length)
                {
                    message = $"expected ')' to close the transform function '{name}'";
                    return false;
                }

                if (span[position] == ')')
                {
                    position++;
                    break;
                }

                int numberStart = position;
                if (!SvgNumberParser.TryRead(span, ref position, out double value))
                {
                    message =
                        $"expected a number in the transform function '{name}' at character "
                            + $"offset {numberStart}";
                    return false;
                }

                if (!double.IsFinite(value))
                {
                    message =
                        $"the transform function '{name}' has a non-finite argument at character "
                            + $"offset {numberStart}";
                    return false;
                }

                arguments.Add(value);
            }

            if (!TryCreate(name, arguments, out SvgAffine next, out message))
            {
                transform = SvgAffine.Identity;
                return false;
            }

            transform = SvgAffine.Multiply(transform, next);
        }

        return true;
    }

    private static bool TryCreate(
        string name,
        List<double> arguments,
        out SvgAffine transform,
        out string message)
    {
        transform = SvgAffine.Identity;
        message = string.Empty;

        switch (name)
        {
            case "matrix":
                if (arguments.Count != 6)
                {
                    message = FormatArgumentCount(name, "6", arguments.Count);
                    return false;
                }

                transform = new SvgAffine(
                    arguments[0],
                    arguments[1],
                    arguments[2],
                    arguments[3],
                    arguments[4],
                    arguments[5]);
                return true;

            case "translate":
                if (arguments.Count is not (1 or 2))
                {
                    message = FormatArgumentCount(name, "1 or 2", arguments.Count);
                    return false;
                }

                transform = SvgAffine.Translate(
                    arguments[0],
                    arguments.Count == 2 ? arguments[1] : 0);
                return true;

            case "scale":
                if (arguments.Count is not (1 or 2))
                {
                    message = FormatArgumentCount(name, "1 or 2", arguments.Count);
                    return false;
                }

                transform = SvgAffine.Scale(
                    arguments[0],
                    arguments.Count == 2 ? arguments[1] : arguments[0]);
                return true;

            case "rotate":
                if (arguments.Count is not (1 or 3))
                {
                    message = FormatArgumentCount(name, "1 or 3", arguments.Count);
                    return false;
                }

                transform = arguments.Count == 3
                    ? SvgAffine.Rotate(arguments[0], arguments[1], arguments[2])
                    : SvgAffine.Rotate(arguments[0], 0, 0);
                return true;

            case "skewX":
                if (arguments.Count != 1)
                {
                    message = FormatArgumentCount(name, "1", arguments.Count);
                    return false;
                }

                transform = SvgAffine.SkewX(arguments[0]);
                return true;

            case "skewY":
                if (arguments.Count != 1)
                {
                    message = FormatArgumentCount(name, "1", arguments.Count);
                    return false;
                }

                transform = SvgAffine.SkewY(arguments[0]);
                return true;

            default:
                message = $"unknown transform function '{name}'";
                return false;
        }
    }

    private static string FormatArgumentCount(string name, string expected, int actual)
        => $"the transform function '{name}' requires {expected} argument(s) but has {actual}";
}
