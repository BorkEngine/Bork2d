using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Internal test seam that observes the pipeline's staging state after each pack is staged.
/// </summary>
/// <remarks>
/// The production pipeline installs no probe. It exists so a regression test can prove that the bake
/// composes one pack at a time and releases its <see cref="PackBuilder"/> as soon as the pack is
/// staged, without relying on a garbage-collection or weak-reference observation.
/// </remarks>
internal interface IPackStagingProbe
{
    /// <summary>Records one staged pack.</summary>
    /// <param name="packIndex">The zero-based pack order.</param>
    /// <param name="stagedPackCount">The number of packs staged so far in this run.</param>
    /// <param name="retainedBuilder">
    /// The builder the pipeline still retains for the pack after staging, or <see langword="null"/>
    /// when it was released as required.
    /// </param>
    void OnPackStaged(int packIndex, int stagedPackCount, PackBuilder? retainedBuilder);
}
