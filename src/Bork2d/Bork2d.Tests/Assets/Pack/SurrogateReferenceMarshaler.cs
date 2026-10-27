using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>Marshals <see cref="SurrogateReference"/> as its typed asset path.</summary>
internal sealed class SurrogateReferenceMarshaler : IMarshaler<SurrogateReference, AssetRef<PackTestData>>
{
    /// <inheritdoc/>
    public AssetRef<PackTestData> Marshal(SurrogateReference value) => value.Target;

    /// <inheritdoc/>
    public SurrogateReference Unmarshal(AssetRef<PackTestData> surrogate) => new(surrogate);
}
