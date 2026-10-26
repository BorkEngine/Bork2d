using Bork2d.Api.Assets;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A generated-shape asset with references in properties, collections, dictionaries, and a nested
/// object; used by the pack-time reference validation tests.
/// </summary>
[GenerateShape]
internal sealed partial class ReferencePackData
{
    /// <summary>Gets or sets a single MessagePack asset reference.</summary>
    public AssetRef<PackTestData> Asset { get; set; }

    /// <summary>Gets or sets a single raw-data reference.</summary>
    public DataRef Data { get; set; }

    /// <summary>Gets or sets a list of asset references.</summary>
    public List<AssetRef<PackTestData>>? Assets { get; set; }

    /// <summary>Gets or sets a dictionary of raw-data references.</summary>
    public Dictionary<string, DataRef>? DataByName { get; set; }

    /// <summary>Gets or sets a nested object containing a reference.</summary>
    public ReferenceNested? Nested { get; set; }
}
