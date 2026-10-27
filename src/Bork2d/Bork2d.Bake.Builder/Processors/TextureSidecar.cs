using System.ComponentModel;
using PolyType;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The settings sidecar of the built-in texture processor: the sampler filter and wrap mode.
/// </summary>
/// <remarks>
/// <para>
/// A texture without a sidecar gets one created from the builder's <c>WithTextureDefaults</c> values
/// on the first bake. An existing sidecar is bound through the shared shape-driven TOML binder, so
/// only the two settings are accepted; any other key is a <c>BK2xxx</c> unknown-key error.
/// </para>
/// <para>
/// The property names are the canonical camelCase keys <c>filter</c> and <c>wrap</c>, and enum values
/// are written and bound as their camelCase member names (<c>nearest</c>/<c>linear</c> and
/// <c>clamp</c>/<c>repeat</c>).
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TextureSidecar
{
    /// <summary>Gets or sets the sampler filter.</summary>
    [DefaultValue(TextureFilter.Linear)]
    public TextureFilter Filter { get; set; } = TextureFilter.Linear;

    /// <summary>Gets or sets the sampler wrap mode.</summary>
    public TextureWrap Wrap { get; set; } = TextureWrap.Clamp;
}
