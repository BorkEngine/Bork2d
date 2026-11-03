using Bork2d.Engine.Rendering;
using Bork2d.Render.Shaders;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The registered sprite render pass: it plans the snapshot's sprites through the allocation-free
/// <see cref="SpriteFramePlanner"/> and records one instanced draw per non-empty camera with the
/// default shader pipeline for the target format.
/// </summary>
/// <remarks>
/// <para>
/// The pass owns the planner and the <see cref="SpriteFramePlan"/> it refills, so planning stays
/// allocation-free after warm-up. During planning the pass resolves the default pipeline for the
/// target format once, adds its plan counts to the shared <see cref="RenderPassStats"/>, and keeps
/// that pipeline for recording. During recording it records through
/// <see cref="SpritePassRecorder.RecordPasses"/> with its own <see cref="SpritePipeline"/>, the frame
/// descriptor set bound with dynamic offset zero, the target view/image, and the planned format.
/// </para>
/// <para>
/// The full-target frame clear is not this pass: the target driver records it once before the
/// registry records any pass, because it defines the target every pass loads from. Render-thread-only.
/// </para>
/// </remarks>
internal sealed class SpriteRenderPass : IRenderPass
{
    private readonly SpritePipeline _pipeline;
    private readonly ShaderStore _shaders;
    private readonly SpriteFramePlanner _planner;
    private SpriteFramePlan _plan = new();
    private Pipeline _plannedPipeline;

    /// <summary>Initializes a new instance of the <see cref="SpriteRenderPass"/> class.</summary>
    /// <param name="pipeline">The sprite pipeline owner created during target startup.</param>
    /// <param name="shaders">The default shader store created during target startup.</param>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal SpriteRenderPass(
        SpritePipeline pipeline,
        ShaderStore shaders,
        TexturePageStore pages,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(logger);
        _pipeline = pipeline;
        _shaders = shaders;
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
        IGlobalUniformBuffer globals,
        Format targetFormat,
        RenderPassStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        SpriteFramePlan plan = _planner.Plan(
            snapshot,
            framebufferWidth,
            framebufferHeight,
            instances,
            globals);
        _plan = plan;
        _plannedPipeline = _pipeline.GetOrCreatePipeline(targetFormat, _shaders.FragmentModule);
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
            _plannedPipeline,
            context.DescriptorSet,
            context.GlobalDescriptorSet,
            context.AttachmentView,
            context.AttachmentImage,
            _plan.Cameras,
            _plan.CameraPlanCount,
            dynamicOffset: 0);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _plan.Reset();
        _plannedPipeline = default;
    }
}
