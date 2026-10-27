using PolyType;

namespace Bork2d.Tests.Bake.Builder;

/// <summary>A processor sidecar shape used to prove the required-sidecar adapter configuration.</summary>
[GenerateShape]
public sealed partial class TestSidecar
{
    /// <summary>Gets or sets a sample setting.</summary>
    public int Value { get; set; }
}
