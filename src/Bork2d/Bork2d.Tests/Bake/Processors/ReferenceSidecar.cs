using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A test sidecar shape that declares references, proving sidecars reject
/// <c>AssetRef</c>/<c>DataRef</c> members.
/// </summary>
[GenerateShape]
public sealed partial class ReferenceSidecar
{
    /// <summary>Gets or sets a typed asset reference that sidecars do not support.</summary>
    public AssetRef<TextureDescriptor> Texture { get; set; }

    /// <summary>Gets or sets a raw-data reference that sidecars do not support.</summary>
    public DataRef Payload { get; set; }
}
