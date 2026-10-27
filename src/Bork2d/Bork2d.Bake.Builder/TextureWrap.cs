namespace Bork2d.Bake.Builder;

/// <summary>
/// The default sampler wrap mode for textures that have no sidecar.
/// </summary>
public enum TextureWrap
{
    /// <summary>Clamp to the edge texel.</summary>
    Clamp,

    /// <summary>Repeat the texture.</summary>
    Repeat,
}
