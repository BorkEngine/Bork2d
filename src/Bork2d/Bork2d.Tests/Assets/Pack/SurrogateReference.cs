using Bork2d.Engine.Assets;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A marshaled value whose surrogate is an asset reference. It proves the collector visits the
/// surrogate instead of treating it as a scalar leaf.
/// </summary>
[TypeShape(Marshaler = typeof(SurrogateReferenceMarshaler))]
internal readonly record struct SurrogateReference(AssetRef<PackTestData> Target);
