namespace Bork2d.Bake.Svg;

/// <summary>
/// The stable SVG parse and signed distance field bake diagnostic codes. The codes live in the bake
/// <c>BK9xxx</c> range; the shader and texture codes in that range are allocated by
/// <c>BakeDiagnosticCodes</c>.
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

    /// <summary>
    /// An SDF bake option is invalid: the resolution is not in <c>1..8192</c>, the range is not
    /// positive, the padding is negative, the margins leave no usable artboard, or the compression
    /// value is undefined.
    /// </summary>
    internal const string SdfOptionsInvalid = "BK9204";

    /// <summary>The document has no drawable fill geometry.</summary>
    internal const string NoGeometry = "BK9205";

    /// <summary>
    /// The signed distance field cannot represent the shape: the shape failed SeasonMSDF validation,
    /// a pixel-mapped geometry point is not finite, the generated field contains a non-finite value,
    /// the field is inside or outside at every texel, the silhouette disagrees with the exact
    /// distance, or a distance-preserving mip level does not preserve the sign.
    /// </summary>
    internal const string SdfBakeFailed = "BK9206";

    /// <summary>
    /// Opt-in BC7 encoding changed the field beyond the accepted level-0 fidelity or flipped the
    /// decoded sign on a level whose long axis is at least four pixels. No uncompressed fallback is
    /// emitted.
    /// </summary>
    internal const string SdfCompressionFailed = "BK9207";
}
