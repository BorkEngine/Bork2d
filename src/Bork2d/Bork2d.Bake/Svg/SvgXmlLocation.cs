using System.Xml;
using System.Xml.Linq;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Reads the 1-based source location that <see cref="XDocument"/> records when it is loaded with
/// <see cref="LoadOptions.SetLineInfo"/>.
/// </summary>
internal static class SvgXmlLocation
{
    /// <summary>Gets the element's 1-based line and column, when the document carries line info.</summary>
    /// <param name="element">The element, or <see langword="null"/>.</param>
    /// <returns>The line and column, or two <see langword="null"/> values.</returns>
    internal static (int? Line, int? Column) Get(XElement? element)
    {
        if (element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            return (lineInfo.LineNumber, lineInfo.LinePosition);
        }

        return (null, null);
    }

    /// <summary>Converts a parser position to a 1-based location component.</summary>
    /// <param name="value">The position; zero or negative means unknown.</param>
    /// <returns>The position, or <see langword="null"/> when it is not positive.</returns>
    internal static int? Positive(int value) => value > 0 ? value : null;
}
