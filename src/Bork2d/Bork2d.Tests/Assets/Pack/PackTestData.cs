using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A generated-shape MessagePack payload used by the pack round-trip tests.
/// </summary>
[GenerateShape]
internal sealed partial class PackTestData
{
    /// <summary>Gets or sets the test document name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the test document value.</summary>
    public int Value { get; set; }

    /// <summary>Gets or sets the test document scale.</summary>
    public float Scale { get; set; }
}
