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
/// <see cref="CameraPlanCount"/>, <see cref="CulledSprites"/>, <see cref="CulledFonts"/>, and
/// <see cref="CulledLines"/> are aggregated for completeness even though the public snapshot does not
/// expose the culled counters. Render-thread-only: the registry owns the single instance and never
/// shares it with the tick thread.
/// </para>
/// </remarks>
internal sealed class RenderPassStats
{
    /// <summary>Gets or sets the number of cameras captured by the snapshot, including invalid ones.</summary>
    internal int CameraCount { get; set; }

    /// <summary>Gets or sets the number of cameras with a valid projection (the pass candidates).</summary>
    internal int CameraPlanCount { get; set; }

    /// <summary>Gets or sets the number of 2D draw calls the frame records (one per run).</summary>
    internal int DrawCalls { get; set; }
    /// <summary>Gets or sets the number of cameras that record at least one draw run.</summary>
    internal int NonEmptyCameras { get; set; }

    /// <summary>Gets or sets the total number of sprite instance records written into the frame buffer.</summary>
    internal int InstanceCount { get; set; }

    /// <summary>Gets or sets the total number of glyph instance records written into the shared buffer.</summary>
    internal int GlyphCount { get; set; }

    /// <summary>Gets or sets the total number of line records submitted by the frame across cameras.</summary>
    internal int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the number of sprites skipped by the frame (null/layer/non-Quad/pending shader
    /// or page).
    /// </summary>
    internal long SkippedSprites { get; set; }

    /// <summary>
    /// Gets or sets the number of fonts skipped by the frame (null data, invalid layer, or a
    /// still-loading custom shader or atlas page). An empty layout is not a skip.
    /// </summary>
    internal long SkippedFonts { get; set; }

    /// <summary>
    /// Gets or sets the number of lines skipped by the frame (null geometry, invalid layer, or a
    /// still-loading custom shader).
    /// </summary>
    internal long SkippedLines { get; set; }

    /// <summary>Gets or sets the number of sprites whose world bounds cull them from every camera.</summary>
    internal int CulledSprites { get; set; }

    /// <summary>Gets or sets the number of fonts whose world bounds cull them from every camera.</summary>
    internal int CulledFonts { get; set; }

    /// <summary>Gets or sets the number of lines whose world bounds cull them from every camera.</summary>
    internal int CulledLines { get; set; }

    /// <summary>Clears every aggregate count before a new planning round.</summary>
    internal void Reset()
    {
        CameraCount = 0;
        CameraPlanCount = 0;
        DrawCalls = 0;
        NonEmptyCameras = 0;
        InstanceCount = 0;
        GlyphCount = 0;
        LineCount = 0;
        SkippedSprites = 0;
        SkippedFonts = 0;
        SkippedLines = 0;
        CulledSprites = 0;
        CulledFonts = 0;
        CulledLines = 0;
    }
}
