using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Render.TwoDimensional;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The registered final present pass: it samples the linear scene color image and writes the
/// sRGB-encoded color into the UNORM swapchain or offscreen target.
/// </summary>
/// <remarks>
/// <para>
/// The pass owns no plan: planning only adds its one draw to the aggregate, and recording always
/// records exactly one fullscreen draw into the target the target driver supplied. It runs after the
/// 2D pass, so it observes every camera clear and sprite draw of the frame and its output is what
/// frame capture and presentation observe.
/// </para>
/// <para>
/// The target driver creates the present pipeline for the final target format during target setup,
/// and recreates it when the windowed swapchain selects a different format, so recording is
/// allocation-free and never creates a Vulkan object. Render-thread-only.
/// </para>
/// </remarks>
internal sealed class PresentRenderPass : IRenderPass
{
    private readonly PresentPipeline _pipeline;

    /// <summary>Initializes a new instance of the <see cref="PresentRenderPass"/> class.</summary>
    /// <param name="pipeline">The present pipeline owner created during target startup.</param>
    internal PresentRenderPass(PresentPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <inheritdoc/>
    public string Name => "Present";

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

        // The present pass has no plan: it always records one fullscreen draw into the final target.
        stats.DrawCalls += 1;
    }

    /// <inheritdoc/>
    public void Record(in RenderPassRecordContext context)
        => PresentPassRecorder.Record(
            context.Vk,
            context.CommandBuffer,
            _pipeline,
            context.PresentDescriptorSet,
            context.SceneImage,
            context.TargetView,
            context.TargetFormat,
            context.TargetExtent);

    /// <inheritdoc/>
    public void Reset()
    {
    }
}
