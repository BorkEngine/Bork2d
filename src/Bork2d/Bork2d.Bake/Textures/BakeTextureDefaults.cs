namespace Bork2d.Bake.Textures;

/// <summary>
/// The mutable sampler defaults shared by the builder configuration and the built-in texture
/// processor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Baking.BakeProjectDefinition"/> owns one instance. <c>WithTextureDefaults</c> updates it
/// in place, and the built-in <c>TextureProcessor</c> holds the same reference, so the defaults apply
/// even when <c>WithTextureDefaults</c> is called after <c>UseDefaultProcessors</c>.
/// </para>
/// <para>
/// The values are copied into the immutable <see cref="Baking.BakeProject"/> by
/// <see cref="Baking.BakeProjectFactory"/>, so a validated project is unaffected by later builder
/// calls.
/// </para>
/// </remarks>
internal sealed class BakeTextureDefaults
{
    /// <summary>Gets or sets the default sampler filter; <see cref="BakeTextureFilter.Linear"/> by default.</summary>
    internal BakeTextureFilter Filter { get; set; } = BakeTextureFilter.Linear;

    /// <summary>Gets or sets the default sampler wrap mode; <see cref="BakeTextureWrap.Clamp"/> by default.</summary>
    internal BakeTextureWrap Wrap { get; set; } = BakeTextureWrap.Clamp;
}
