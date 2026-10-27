using Bork2d.Engine.Rendering;
using PolyType;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// A generated-shape asset with a <see cref="Color"/> member. Its shape exercises the D-M4-12
/// marshaler path and the <c>AssetReferenceCollector.VisitSurrogate</c> regression.
/// </summary>
[GenerateShape]
internal sealed partial class ColorPackData
{
    /// <summary>Gets or sets a linear color.</summary>
    public Color Tint { get; set; }
}
