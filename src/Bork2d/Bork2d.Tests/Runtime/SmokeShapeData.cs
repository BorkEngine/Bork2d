using Bork2d.Engine.Rendering;
using PolyType;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// The tests-local generated-shape document used to prove the Nerdbank.MessagePack
/// source-generated policy without a reflection resolver.
/// </summary>
[GenerateShape]
internal sealed partial class SmokeShapeData
{
    /// <summary>Gets or sets the smoke document name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the smoke document value.</summary>
    public int Value { get; set; }

    /// <summary>Gets or sets the smoke document scale.</summary>
    public float Scale { get; set; }

    /// <summary>Gets or sets a linear color serialized by <c>ColorMarshaler</c>.</summary>
    public Color Tint { get; set; }
}
