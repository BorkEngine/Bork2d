namespace Bork2d.Bake.Textures;

/// <summary>
/// The sampler wrap mode used by the built-in texture processor when it creates a default sidecar.
/// </summary>
internal enum BakeTextureWrap
{
    /// <summary>Clamp to the edge texel.</summary>
    Clamp,

    /// <summary>Repeat the texture.</summary>
    Repeat,
}
