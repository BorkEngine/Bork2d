namespace Bork2d.Bake.Textures;

/// <summary>
/// The sampler filter used by the built-in texture processor when it creates a default sidecar.
/// </summary>
internal enum BakeTextureFilter
{
    /// <summary>Nearest-neighbor sampling.</summary>
    Nearest,

    /// <summary>Linear sampling.</summary>
    Linear,
}
