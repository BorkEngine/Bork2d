using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A processor output type carrying an <c>AssetRef&lt;TextureDescriptor&gt;</c> and a
/// <c>DataRef</c>, used to prove processor-output references get the same validation as data
/// references.
/// </summary>
[GenerateShape]
public sealed partial class ProcessorRefAsset
{
    /// <summary>Gets or sets the referenced typed asset.</summary>
    public AssetRef<TextureDescriptor> Texture { get; set; }

    /// <summary>Gets or sets the referenced raw payload.</summary>
    public DataRef Payload { get; set; }
}
