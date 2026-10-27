namespace Bork2d.Bake.Textures;

/// <summary>
/// The default sampler filter for texture imports when a sidecar omits <c>filter</c>.
/// </summary>
internal enum BakeTextureFilter
{
    /// <summary>Nearest-neighbor sampling.</summary>
    Nearest,

    /// <summary>Linear sampling.</summary>
    Linear,
}
