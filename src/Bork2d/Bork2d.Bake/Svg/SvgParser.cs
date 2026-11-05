using System.Xml;
using System.Xml.Linq;

namespace Bork2d.Bake.Svg;

/// <summary>
/// Parses one SVG document into an <see cref="SvgArtwork"/>.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is the root <c>svg</c> element, <c>g</c>, <c>path</c>, <c>rect</c>,
/// <c>circle</c>, <c>ellipse</c>, <c>line</c>, <c>polyline</c>, and <c>polygon</c>, with
/// <c>viewBox</c>, <c>width</c>/<c>height</c>, <c>transform</c>, <c>fill</c>, and <c>fill-rule</c>.
/// Elements are matched by local name, so a prefixed SVG namespace still parses. Paint, stroke,
/// opacity, color, gradients, filters, masks, clip paths, text, images, CSS, <c>defs</c>, and
/// <c>use</c> are ignored; <c>defs</c> and <c>use</c> are not expanded. An unsupported element and
/// its subtree are skipped with one informational diagnostic, while <c>title</c>, <c>desc</c>, and
/// <c>metadata</c> are ignored silently because they never draw.
/// </para>
/// <para>
/// Malformed input never throws: XML failures are <c>BK9201</c>, invalid supported content is
/// <c>BK9202</c>, ignored features are <c>BK9203</c>, and a document without fillable geometry is
/// <c>BK9205</c>. The parser returns <see langword="null"/> as soon as an error is collected.
/// Callers decode the bytes as strict UTF-8 before calling; this method takes decoded text.
/// </para>
/// </remarks>
internal static class SvgParser
{
    /// <summary>Parses one decoded SVG document.</summary>
    /// <param name="text">The decoded source text.</param>
    /// <param name="sourceDisplayPath">
    /// The source path used in the malformed-XML message; the caller owns location formatting.
    /// </param>
    /// <param name="diagnostics">The bag that receives the diagnostics; its errors make the result null.</param>
    /// <returns>The artwork, or <see langword="null"/> when the source cannot be parsed.</returns>
    internal static SvgArtwork? TryParse(
        string text,
        string sourceDisplayPath,
        SvgDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceDisplayPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        XDocument document;
        try
        {
            using var reader = new StringReader(text);
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            diagnostics.Error(
                SvgDiagnosticCodes.SourceXmlInvalid,
                $"The SVG source '{sourceDisplayPath}' is not well-formed XML: {exception.Message}",
                SvgXmlLocation.Positive(exception.LineNumber),
                SvgXmlLocation.Positive(exception.LinePosition));
            return null;
        }

        XElement? root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.Ordinal))
        {
            (int? line, int? column) = SvgXmlLocation.Get(root);
            string found = root is null
                ? "no root element"
                : $"the root element '{root.Name.LocalName}'";
            diagnostics.Error(
                SvgDiagnosticCodes.SourceXmlInvalid,
                $"The SVG source '{sourceDisplayPath}' does not have an 'svg' root element; found "
                    + $"{found}.",
                line,
                column);
            return null;
        }

        return new SvgParseContext(diagnostics).Parse(root);
    }
}
