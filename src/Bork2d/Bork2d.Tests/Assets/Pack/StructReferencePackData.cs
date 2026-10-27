using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A generated-shape asset that holds a typed reference to a value-type root. It proves the relaxed
/// <see cref="AssetRef{TAsset}"/> constraint and the path-string marshaler for struct targets.
/// </summary>
[GenerateShape]
internal sealed partial class StructReferencePackData
{
    /// <summary>Gets or sets a reference to a value-type asset root.</summary>
    public AssetRef<PackTestStruct> Boss { get; set; }
}
