using PolyType;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The settings sidecar of the built-in texture processor: the sampler filter and wrap mode.
/// </summary>
/// <remarks>
/// <para>
/// Both members are optional so an existing sidecar can override one setting and inherit the other
/// from the builder's <c>WithTextureDefaults</c> values. A texture without a sidecar gets one created
/// with both keys from those defaults on the first bake.
/// </para>
/// <para>
/// The property names are the canonical camelCase keys <c>filter</c> and <c>wrap</c>, and enum values
/// are written and bound as their camelCase member names (<c>nearest</c>/<c>linear</c> and
/// <c>clamp</c>/<c>repeat</c>). An absent key stays <see langword="null"/> and falls back to the
/// builder default; a key present in the file always overrides it.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TextureSidecar
{
    /// <summary>Gets or sets the sampler filter, or <see langword="null"/> to use the builder default.</summary>
    public TextureFilter? Filter { get; set; }

    /// <summary>Gets or sets the sampler wrap mode, or <see langword="null"/> to use the builder default.</summary>
    public TextureWrap? Wrap { get; set; }
}
