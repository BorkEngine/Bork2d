namespace Bork2d.Bake.Svg;

/// <summary>
/// Identifies the SVG parser, MSDF generator, and SDF mip versions used by the bake.
/// </summary>
/// <remarks>
/// <see cref="ConfigurationMarker"/> participates in the incremental configuration hash, so changing
/// the parser, upgrading SeasonMSDF, or changing the SDF mip handling invalidates every cached SVG
/// texture payload instead of reusing bytes produced by a different tool.
/// </remarks>
internal static class SvgToolInfo
{
    /// <summary>The SVG parser version, bumped when the normalized geometry semantics change.</summary>
    internal const string SvgParserVersion = "svg-1";

    /// <summary>The selected MSDF generator package version.</summary>
    internal const string MsdfGeneratorVersion = "seasonmsdf-0.2.0";

    /// <summary>The SDF mip handling version, bumped when the distance-preserving chain changes.</summary>
    internal const string SdfMipVersion = "sdfmips-1";

    /// <summary>
    /// Gets the marker recorded in the incremental configuration hash:
    /// <c>svg-1/seasonmsdf-0.2.0/sdfmips-1</c>.
    /// </summary>
    internal static string ConfigurationMarker { get; } =
        $"{SvgParserVersion}/{MsdfGeneratorVersion}/{SdfMipVersion}";
}
