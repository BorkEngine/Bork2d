namespace Bork2d.Bake.Textures;

/// <summary>
/// The default sampler wrap mode for texture imports when a sidecar omits <c>wrap</c>.
/// </summary>
internal enum BakeTextureWrap
{
    /// <summary>Clamp to the edge texel.</summary>
    Clamp,

    /// <summary>Repeat the texture.</summary>
    Repeat,
}
