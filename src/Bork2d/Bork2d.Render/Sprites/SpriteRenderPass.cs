using Bork2d.Engine.Rendering;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The registered sprite render pass: it plans the snapshot's sprites through the allocation-free
/// <see cref="SpriteFramePlanner"/> and records one instanced draw per non-empty camera.
/// </summary>
/// <remarks>
/// <para>
/// The pass owns the planner and the <see cref="SpriteFramePlan"/> it refills, so planning stays
/// allocation-free after warm-up. During planning the pass adds its plan counts to the shared
/// <see cref="RenderPassStats"/>; during recording it records through
/// <see cref="SpritePassRecorder.RecordPasses"/> with its own <see cref="SpritePipeline"/>, the frame
/// descriptor set, the target view/image, and the target format from the record context.
/// </para>
/// <para>
/// The full-target frame clear is not this pass: the target driver records it once before the
/// registry records any pass, because it defines the target every pass loads from. Render-thread-only.
/// </para>
/// </remarks>
internal sealed class SpriteRenderPass : IRenderPass
{
    private readonly SpritePipeline _pipeline;
    private readonly SpriteFramePlanner _planner;
    private SpriteFramePlan _plan = new();

    /// <summary>Initializes a new instance of the <see cref="SpriteRenderPass"/> class.</summary>
    /// <param name="pipeline">The sprite pipeline owner created during target startup.</param>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal SpriteRenderPass(SpritePipeline pipeline, TexturePageStore pages, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(logger);
        _pipeline = pipeline;
        _planner = new SpriteFramePlanner(pages, logger);
    }

    /// <inheritdoc/>
    public string Name => "Sprite";

    /// <inheritdoc/>
    public void Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances,
        RenderPassStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        SpriteFramePlan plan = _planner.Plan(snapshot, framebufferWidth, framebufferHeight, instances);
        _plan = plan;
        stats.CameraCount += plan.CameraCount;
        stats.CameraPlanCount += plan.CameraPlanCount;
        stats.DrawCalls += plan.DrawCalls;
        stats.NonEmptyCameras += plan.NonEmptyCameras;
        stats.InstanceCount += plan.InstanceCount;
        stats.SkippedSprites += plan.SkippedSprites;
        stats.CulledSprites += plan.CulledSprites;
    }

    /// <inheritdoc/>
    public void Record(in RenderPassRecordContext context)
    {
        SpritePassRecorder.RecordPasses(
            context.Vk,
            context.CommandBuffer,
            _pipeline,
            _pipeline.GetOrCreatePipeline(context.TargetFormat),
            context.DescriptorSet,
            context.AttachmentView,
            context.AttachmentImage,
            _plan.Cameras,
            _plan.CameraPlanCount);
    }

    /// <inheritdoc/>
    public void Reset() => _plan.Reset();
}
