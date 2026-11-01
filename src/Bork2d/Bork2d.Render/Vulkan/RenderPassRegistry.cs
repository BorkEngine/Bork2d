using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The renderer's ordered render-pass list and shared per-frame statistics aggregate.
/// </summary>
/// <remarks>
/// <para>
/// Both target drivers create one registry during startup with the passes they support (currently
/// the sprite pass only), then call <see cref="Plan"/> and <see cref="Record"/> from their own frame
/// loops. Planning resets the aggregate and each pass's per-frame state, then runs the passes in
/// registration order; recording runs the same passes in the same order. Pass order is therefore
/// deterministic, and adding a renderable kind registers a producer plus one pass instead of editing
/// a central plan/record switch.
/// </para>
/// <para>
/// Render-thread-only. The registry owns no Vulkan object; the target driver owns the resources it
/// passes through <see cref="RenderPassRecordContext"/> and keeps the passes alive for the run.
/// </para>
/// </remarks>
internal sealed class RenderPassRegistry
{
    private readonly IRenderPass[] _passes;
    private readonly RenderPassStats _stats = new();

    /// <summary>Initializes a new instance of the <see cref="RenderPassRegistry"/> class.</summary>
    /// <param name="passes">The passes in deterministic planning and recording order.</param>
    /// <exception cref="ArgumentNullException">A pass is <see langword="null"/>.</exception>
    internal RenderPassRegistry(params IRenderPass[] passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        for (var index = 0; index < passes.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(passes[index]);
        }

        _passes = passes;
    }

    /// <summary>Gets the registered passes in planning and recording order.</summary>
    internal IReadOnlyList<IRenderPass> Passes => _passes;

    /// <summary>Gets the most recently planned frame's aggregate statistics.</summary>
    internal RenderPassStats Stats => _stats;

    /// <summary>
    /// Resets the aggregate and each pass's per-frame state, then plans one snapshot through every
    /// registered pass in order.
    /// </summary>
    /// <param name="snapshot">The consumed snapshot; read-only and never mutated.</param>
    /// <param name="framebufferWidth">The target/framebuffer width in pixels.</param>
    /// <param name="framebufferHeight">The target/framebuffer height in pixels.</param>
    /// <param name="instances">The frame's instance sink, after its fence has been observed.</param>
    /// <exception cref="Bork2d.Engine.Bork2dException">A pass's plan is malformed or cannot fit.</exception>
    internal void Plan(
        FrameSnapshot snapshot,
        int framebufferWidth,
        int framebufferHeight,
        IInstanceBuffer instances)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(instances);

        _stats.Reset();
        for (var index = 0; index < _passes.Length; index++)
        {
            IRenderPass pass = _passes[index];
            pass.Reset();
            pass.Plan(snapshot, framebufferWidth, framebufferHeight, instances, _stats);
        }
    }

    /// <summary>Records every registered pass in order into the frame command buffer.</summary>
    /// <param name="context">The target resources the recording may use.</param>
    internal void Record(in RenderPassRecordContext context)
    {
        for (var index = 0; index < _passes.Length; index++)
        {
            _passes[index].Record(in context);
        }
    }
}
