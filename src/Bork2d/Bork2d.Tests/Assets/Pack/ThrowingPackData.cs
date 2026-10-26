using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A generated-shape type whose getter throws, used to prove serialization failures surface as
/// <c>Bork2dException</c> from the pack builder.
/// </summary>
[GenerateShape]
internal sealed partial class ThrowingPackData
{
    /// <summary>Gets a value that always throws.</summary>
    public string Name
    {
        get => throw new InvalidOperationException("serialization boom");
        set { }
    }
}
