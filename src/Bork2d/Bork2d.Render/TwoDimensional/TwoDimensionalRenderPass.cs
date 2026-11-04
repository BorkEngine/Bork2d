using Bork2d.Engine.Rendering;
using Bork2d.Render.Shaders;
using Bork2d.Render.Sprites;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The registered unified 2D render pass: it plans the snapshot's sprites and lines together through
/// the allocation-free <see cref="TwoDimensionalFramePlanner"/>, resolves each run's cached pipeline
/// for that run's kind and the linear scene format, and records the ordered sprite and line draws.
/// </summary>
/// <remarks>
/// <para>
/// The pass owns the planner and the <see cref="TwoDimensionalFramePlan"/> it refills, so planning
/// stays allocation-free after warm-up. During planning the pass resolves every run's pipeline for
/// the scene format from the shader catalog (the planner already validated/created it), adds its plan
/// counts to the shared <see cref="RenderPassStats"/>, and keeps the plan for recording. During
/// recording it records through <see cref="TwoDimensionalPassRecorder.RecordPasses"/> with its own
/// <see cref="TwoDimensionalPipeline"/>, the frame descriptor sets, the frame line vertex/index
/// buffers, the linear scene view/image, and the planned run pipelines.
/// </para>
/// <para>
/// The full-target frame clear is not this pass: the target driver records it once into the scene
/// image before the registry records any pass, because it defines the scene every pass loads from.
/// The final present pass runs after this pass and encodes the scene into the swapchain/offscreen
/// target. Render-thread-only.
/// </para>
/// </remarks>
internal sealed class TwoDimensionalRenderPass : IRenderPass
{
    private readonly TwoDimensionalPipeline _pipeline;
    private readonly ShaderStore _shaders;
    private readonly TwoDimensionalFramePlanner _planner;
    private TwoDimensionalFramePlan _plan = new();
    private Pipeline[] _runPipelines = [];

    /// <summary>Initializes a new instance of the <see cref="TwoDimensionalRenderPass"/> class.</summary>
    /// <param name="pipeline">The 2D pipeline owner created during target startup.</param>
    /// <param name="shaders">The shader catalog created during target startup.</param>
    /// <param name="pages">The texture-page cache the planner resolves and prefetches through.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger for throttled diagnostics.</param>
    internal TwoDimensionalRenderPass(
        TwoDimensionalPipeline pipeline,
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
        _planner = new TwoDimensionalFramePlanner(pages, shaders, logger);
    }

    /// <inheritdoc/>
    public string Name => "2D";

    /// <inheritdoc/>
    public void Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances,
        ILineBufferSink lines,
        IGlobalUniformBuffer globals,
        Format sceneFormat,
        RenderPassStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        TwoDimensionalFramePlan plan = _planner.Plan(
            snapshot,
            framebufferWidth,
            framebufferHeight,
            instances,
            lines,
            globals,
            sceneFormat);
        _plan = plan;
        EnsureRunPipelineCapacity(plan.RunCount);
        for (var runIndex = 0; runIndex < plan.RunCount; runIndex++)
        {
            DrawRun run = plan.Runs[runIndex];
            _runPipelines[runIndex] = _shaders.GetPipeline(
                run.ShaderEntryId,
                run.Kind == DrawKind.Sprite
                    ? ShaderPipelineKind.Sprite
                    : ShaderPipelineKind.Line,
                sceneFormat);
        }

        stats.CameraCount += plan.CameraCount;
        stats.CameraPlanCount += plan.CameraPlanCount;
        stats.DrawCalls += plan.DrawCalls;
        stats.NonEmptyCameras += plan.NonEmptyCameras;
        stats.InstanceCount += plan.InstanceCount;
        stats.LineCount += plan.LineCount;
        stats.SkippedSprites += plan.SkippedSprites;
        stats.SkippedLines += plan.SkippedLines;
        stats.CulledSprites += plan.CulledSprites;
        stats.CulledLines += plan.CulledLines;
    }

    /// <inheritdoc/>
    public void Record(in RenderPassRecordContext context)
        => TwoDimensionalPassRecorder.RecordPasses(
            context.Vk,
            context.CommandBuffer,
            _pipeline,
            context.DescriptorSet,
            context.GlobalDescriptorSet,
            context.SceneView,
            context.SceneImage,
            context.LineVertexBuffer,
            context.LineIndexBuffer,
            _plan.Cameras,
            _plan.CameraPlanCount,
            _plan.Runs,
            _runPipelines);

    /// <inheritdoc/>
    public void Reset()
    {
        _plan.Reset();
    }

    private void EnsureRunPipelineCapacity(int required)
    {
        if (_runPipelines.Length >= required)
        {
            return;
        }

        int capacity = Math.Max(required, Math.Max(16, _runPipelines.Length * 2));
        Array.Resize(ref _runPipelines, capacity);
    }
}
