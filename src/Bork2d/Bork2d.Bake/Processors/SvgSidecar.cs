using Bork2d.Bake.Svg;
using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The settings sidecar of the built-in SVG processor: the output resolution, the distance spread, the
/// extra margin, and the storage format of the baked multi-channel signed distance field texture.
/// </summary>
/// <remarks>
/// <para>
/// Every member is optional so an existing sidecar can override one setting and inherit the rest from
/// the defaults. An SVG source without a sidecar gets one created with all four explicit default
/// values on the first bake.
/// </para>
/// <para>
/// The property names are the canonical camelCase keys <c>resolution</c>, <c>range</c>,
/// <c>padding</c>, and <c>compression</c>; the compression enum binds and is written as
/// <c>none</c>/<c>bc7</c>. <c>resolution</c> is the maximum output texture dimension in pixels and is
/// clamped by the baker to <c>1..8192</c>; <c>range</c> is the distance spread in pixels and must be
/// positive; <c>padding</c> is the extra margin in pixels beyond the range and must not be negative.
/// An absent key stays <see langword="null"/> and falls back to its default; a key present in the file
/// always overrides it. There is no generic texture channel/compression key: SDF data is always
/// encoded through this processor's own format choice.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class SvgSidecar
{
    /// <summary>
    /// Gets or sets the maximum output texture dimension in pixels, or <see langword="null"/> to use
    /// <see cref="SvgSdfBakeOptions.DefaultResolution"/>.
    /// </summary>
    public int? Resolution { get; set; }

    /// <summary>
    /// Gets or sets the distance spread in pixels, or <see langword="null"/> to use
    /// <see cref="SvgSdfBakeOptions.DefaultRange"/>.
    /// </summary>
    public int? Range { get; set; }

    /// <summary>
    /// Gets or sets the extra margin in pixels beyond the range margin, or <see langword="null"/> to
    /// use <see cref="SvgSdfBakeOptions.DefaultPadding"/>.
    /// </summary>
    public int? Padding { get; set; }

    /// <summary>
    /// Gets or sets the storage format, or <see langword="null"/> to use
    /// <see cref="SdfCompression.None"/>.
    /// </summary>
    public SdfCompression? Compression { get; set; }
}
