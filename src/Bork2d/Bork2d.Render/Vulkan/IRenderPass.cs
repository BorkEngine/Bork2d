using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Render.TwoDimensional;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One registered render pass: it plans the snapshot data it consumes and records its own draws.
/// </summary>
/// <remarks>
/// <para>
/// Passes are registered in one ordered <see cref="RenderPassRegistry"/>. <see cref="Plan"/> runs in
/// registration order and may only read the snapshot and the scene color format, write its own plan
/// and the per-run global uniform blocks, and add its counts to the shared
/// <see cref="RenderPassStats"/>; <see cref="Record"/> runs in the same order, so the recorded draw
/// order is deterministic. A new renderable kind adds an engine producer and one pass here; no
/// central frame-loop plan/record switch changes.
/// </para>
/// <para>
/// A pass owns its pipeline and descriptor specifics and ignores the context members it does not
/// use. All members are render-thread-only: the pass is created during target startup, planned and
/// recorded once per consumed snapshot, and never touched by the tick thread.
/// </para>
/// </remarks>
internal interface IRenderPass
{
    /// <summary>Gets a short diagnostic name for the pass.</summary>
    string Name { get; }

    /// <summary>
    /// Plans the snapshot data this pass consumes and adds its counts to the aggregate.
    /// </summary>
    /// <param name="snapshot">The consumed snapshot; read-only and never mutated.</param>
    /// <param name="framebufferWidth">The scene/target width in pixels.</param>
    /// <param name="framebufferHeight">The scene/target height in pixels.</param>
    /// <param name="instances">The frame's sprite/glyph instance sink, after its fence has been observed.</param>
    /// <param name="lines">The frame's line vertex/index sink, after its fence has been observed.</param>
    /// <param name="globals">
    /// The frame's global uniform sink; the 2D pass writes one block per draw run.
    /// </param>
    /// <param name="sceneFormat">
    /// The linear scene color format the 2D pipelines are created for; it is independent of the final
    /// target format.
    /// </param>
    /// <param name="stats">The registry-owned aggregate this pass contributes to.</param>
    void Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances,
        ILineBufferSink lines,
        IGlobalUniformBuffer globals,
        Format sceneFormat,
        RenderPassStats stats);

    /// <summary>Records this pass's commands into the frame command buffer.</summary>
    /// <param name="context">The target resources the recording may use.</param>
    void Record(in RenderPassRecordContext context);

    /// <summary>
    /// Clears the pass's per-frame planned state; called by the registry before this pass plans.
    /// </summary>
    /// <remarks>
    /// A plan is refilled from scratch each frame, so a pass that is planned again overwrites its
    /// state. The reset guarantees a pass never records a stale plan from an earlier round.
    /// </remarks>
    void Reset();
}
