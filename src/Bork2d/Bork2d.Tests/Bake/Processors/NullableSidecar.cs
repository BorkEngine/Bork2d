using PolyType;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>
/// A settings sidecar with nullable scalar and enum members, used to prove the canonical writer
/// unwraps optional shapes and skips absent values.
/// </summary>
[GenerateShape]
public sealed partial class NullableSidecar
{
    /// <summary>Gets or sets an optional integer setting.</summary>
    public int? Count { get; set; }

    /// <summary>Gets or sets an optional enum setting.</summary>
    public TestSidecarMode? Mode { get; set; }
}
