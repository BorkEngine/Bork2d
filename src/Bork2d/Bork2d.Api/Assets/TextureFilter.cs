namespace Bork2d.Api.Assets;

/// <summary>
/// Selects the sampler filter recorded by a texture descriptor.
/// </summary>
/// <remarks>
/// <see cref="Linear"/> is the default and the zero value. This is sampler state only; v1 does not
/// create GPU samplers from it. The value is stored as an integer in the pack.
/// </remarks>
public enum TextureFilter
{
    /// <summary>Linear sampling.</summary>
    Linear = 0,

    /// <summary>Nearest-neighbor sampling.</summary>
    Nearest = 1,
}
