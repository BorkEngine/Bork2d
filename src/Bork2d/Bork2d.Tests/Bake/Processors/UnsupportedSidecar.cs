using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>A sidecar shape with a collection member, which the canonical sidecar writer cannot write.</summary>
[GenerateShape]
public sealed partial class UnsupportedSidecar
{
    /// <summary>Gets or sets a collection the sidecar writer rejects.</summary>
    public int[] Values { get; set; } = [];
}
