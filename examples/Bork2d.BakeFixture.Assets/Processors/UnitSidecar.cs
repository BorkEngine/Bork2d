using PolyType;

namespace Bork2d.BakeFixture.Assets.Processors;

/// <summary>
/// The settings sidecar of the Unit acceptance processor: an optional name override.
/// </summary>
/// <remarks>
/// The member is optional. When it is missing, or present but empty, the processor uses
/// <c>SourceFileNameWithoutExtension</c>. A <c>.unit</c> source without a sidecar gets one created with
/// the source file stem on the first bake.
/// </remarks>
[GenerateShape]
internal sealed partial class UnitSidecar
{
    /// <summary>Gets or sets the unit name, or <see langword="null"/> to use the source file stem.</summary>
    public string? Name { get; set; }
}
