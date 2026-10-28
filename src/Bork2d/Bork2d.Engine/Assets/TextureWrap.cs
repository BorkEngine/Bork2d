namespace Bork2d.Engine.Assets;

/// <summary>
/// Selects the sampler wrap mode recorded by a texture descriptor.
/// </summary>
/// <remarks>
/// <see cref="Clamp"/> is the default and the zero value. This is sampler state only; v1 does not
/// create GPU samplers from it. The value is stored as an integer in the pack.
/// </remarks>
public enum TextureWrap
{
    /// <summary>Clamp to the edge texel.</summary>
    Clamp = 0,

    /// <summary>Repeat the texture.</summary>
    Repeat = 1,
}
