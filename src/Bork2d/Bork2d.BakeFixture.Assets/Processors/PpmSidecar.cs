using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.BakeFixture.Assets.Processors;

/// <summary>
/// The settings sidecar of the PPM acceptance processor: the sampler filter and wrap mode.
/// </summary>
/// <remarks>
/// Both members are optional so an existing sidecar can override one setting and inherit the other
/// from the values the processor was constructed with. A <c>.ppm</c> source without a sidecar gets one
/// created with both values from those configured defaults on the first bake.
/// </remarks>
[GenerateShape]
internal sealed partial class PpmSidecar
{
    /// <summary>Gets or sets the sampler filter, or <see langword="null"/> to use the configured default.</summary>
    public TextureFilter? Filter { get; set; }

    /// <summary>Gets or sets the sampler wrap mode, or <see langword="null"/> to use the configured default.</summary>
    public TextureWrap? Wrap { get; set; }
}
