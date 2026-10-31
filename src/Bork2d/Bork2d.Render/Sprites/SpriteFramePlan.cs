namespace Bork2d.Render.Sprites;

/// <summary>
/// The renderer-owned result of planning one snapshot: valid cameras in ascending <c>ZIndex</c>
/// order with their contiguous instance ranges, plus the frame's draw/instance/skip totals
/// (M8 §6.6/§6.8).
/// </summary>
/// <remarks>
/// <para>
/// The instance is created once by <see cref="SpriteFramePlanner"/> and refilled by each
/// <c>Plan</c> call, so a warm frame allocates nothing. The renderer records the per-camera passes
/// from <see cref="Cameras"/> immediately after planning and before the next plan overwrites it.
/// </para>
/// <para>
/// <see cref="CameraCount"/> is the snapshot's captured camera count, including cameras that were
/// invalid and skipped; <see cref="CameraPlanCount"/> is the valid subset. <see cref="DrawCalls"/>
/// always equals <see cref="NonEmptyCameras"/> and is at most <see cref="CameraCount"/>.
/// </para>
/// </remarks>
internal sealed class SpriteFramePlan
{
    /// <summary>Gets the number of cameras captured by the snapshot, including invalid ones.</summary>
    internal int CameraCount { get; set; }

    /// <summary>Gets the number of cameras with a valid projection (the pass candidates).</summary>
    internal int CameraPlanCount { get; set; }

    /// <summary>Gets the number of <c>vkCmdDraw</c> calls the frame records.</summary>
    internal int DrawCalls { get; set; }

    /// <summary>Gets the number of cameras that draw at least one instance.</summary>
    internal int NonEmptyCameras { get; set; }

    /// <summary>Gets the total number of instance records written into the frame buffer.</summary>
    internal int InstanceCount { get; set; }

    /// <summary>Gets the number of sprites skipped by the frame (null/layer/non-Quad/pending).</summary>
    internal long SkippedSprites { get; set; }

    /// <summary>Gets the valid per-camera plans, in ascending <c>ZIndex</c> order.</summary>
    internal CameraDrawPlan[] Cameras { get; set; } = [];
}
