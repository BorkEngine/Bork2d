using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A nested generated-shape object used to prove that pack-time reference collection descends into
/// child objects.
/// </summary>
[GenerateShape]
internal sealed partial class ReferenceNested
{
    /// <summary>Gets or sets the nested asset reference.</summary>
    public AssetRef<PackTestData> Asset { get; set; }
}
