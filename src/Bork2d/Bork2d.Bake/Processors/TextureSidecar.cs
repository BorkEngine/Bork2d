using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The settings sidecar of the built-in texture processor: the semantic channel type, compression
/// mode, sampler filter, and wrap mode.
/// </summary>
/// <remarks>
/// <para>
/// Every member is optional so an existing sidecar can override one setting and inherit the rest from
/// the defaults. A texture without a sidecar gets one created with the explicit
/// <c>type = "rgba"</c>, <c>compression = "none"</c>, and the builder's filter/wrap values on the first
/// bake.
/// </para>
/// <para>
/// The property names are the canonical camelCase keys <c>type</c>, <c>compression</c>, <c>filter</c>,
/// and <c>wrap</c>; enum values are written and bound as their camelCase member names
/// (<c>rgba</c>/<c>rgb</c>/<c>rg</c>/<c>r</c>, <c>auto</c>/<c>none</c>, <c>nearest</c>/<c>linear</c>,
/// and <c>clamp</c>/<c>repeat</c>). An absent key stays <see langword="null"/> and falls back to the
/// default; a key present in the file always overrides it. There is no color-space key: every texture
/// is linear.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TextureSidecar
{
    /// <summary>
    /// Gets or sets the semantic channel type, or <see langword="null"/> to use
    /// <see cref="TextureChannelType.Rgba"/>.
    /// </summary>
    public TextureChannelType? Type { get; set; }

    /// <summary>
    /// Gets or sets the compression mode, or <see langword="null"/> to use
    /// <see cref="TextureCompression.None"/>.
    /// </summary>
    public TextureCompression? Compression { get; set; }

    /// <summary>Gets or sets the sampler filter, or <see langword="null"/> to use the builder default.</summary>
    public TextureFilter? Filter { get; set; }

    /// <summary>Gets or sets the sampler wrap mode, or <see langword="null"/> to use the builder default.</summary>
    public TextureWrap? Wrap { get; set; }
}
