namespace Bork2d.Bake.Svg;

/// <summary>
/// The stable SVG parse diagnostic codes. The codes live in the bake <c>BK9xxx</c> range; the shader
/// and texture codes in that range are allocated by <c>BakeDiagnosticCodes</c>.
/// </summary>
internal static class SvgDiagnosticCodes
{
    /// <summary>The source is not well-formed XML, or its root element is not an SVG element.</summary>
    internal const string SourceXmlInvalid = "BK9201";

    /// <summary>
    /// Invalid supported SVG content: a bad <c>viewBox</c>, <c>width</c>, <c>height</c>, length,
    /// transform, fill rule, path data, or geometry attribute, a non-finite number, or a rounded
    /// rectangle.
    /// </summary>
    internal const string ContentInvalid = "BK9202";

    /// <summary>An unsupported element or feature was skipped.</summary>
    internal const string FeatureIgnored = "BK9203";

    /// <summary>The document has no drawable fill geometry.</summary>
    internal const string NoGeometry = "BK9205";
}
