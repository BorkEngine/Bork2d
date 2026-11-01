namespace Bork2d.Render.Vulkan;

/// <summary>
/// The mutable per-frame aggregate every registered render pass contributes to.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RenderPassRegistry"/> resets the aggregate at the start of each planning round, then
/// each pass adds its own plan counts in registration order. The public
/// <see cref="Bork2d.Engine.Rendering.RenderStats"/> snapshot maps from this aggregate, so it is
/// independent of which passes exist and in what order they run.
/// </para>
/// <para>
/// <see cref="CameraPlanCount"/> and <see cref="CulledSprites"/> are aggregated for completeness
/// even though the public snapshot does not expose them. Render-thread-only: the registry owns the
/// single instance and never shares it with the tick thread.
/// </para>
/// </remarks>
internal sealed class RenderPassStats
{
    /// <summary>Gets or sets the number of cameras captured by the snapshot, including invalid ones.</summary>
    internal int CameraCount { get; set; }

    /// <summary>Gets or sets the number of cameras with a valid projection (the pass candidates).</summary>
    internal int CameraPlanCount { get; set; }

    /// <summary>Gets or sets the number of <c>vkCmdDraw</c> calls the frame records.</summary>
    internal int DrawCalls { get; set; }

    /// <summary>Gets or sets the number of cameras that draw at least one instance.</summary>
    internal int NonEmptyCameras { get; set; }

    /// <summary>Gets or sets the total number of instance records written into the frame buffer.</summary>
    internal int InstanceCount { get; set; }

    /// <summary>Gets or sets the number of sprites skipped by the frame (null/layer/non-Quad/pending).</summary>
    internal long SkippedSprites { get; set; }

    /// <summary>Gets or sets the number of sprites whose world bounds cull them from every camera.</summary>
    internal int CulledSprites { get; set; }

    /// <summary>Clears every aggregate count before a new planning round.</summary>
    internal void Reset()
    {
        CameraCount = 0;
        CameraPlanCount = 0;
        DrawCalls = 0;
        NonEmptyCameras = 0;
        InstanceCount = 0;
        SkippedSprites = 0;
        CulledSprites = 0;
    }
}
