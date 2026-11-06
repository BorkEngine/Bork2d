namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The renderer-owned result of planning one snapshot: valid cameras in ascending <c>ZIndex</c> order
/// with their contiguous sprite/glyph instance, line, and draw-run ranges, plus the frame's draw/
/// instance/glyph/line/skip/cull totals.
/// </summary>
/// <remarks>
/// <para>
/// The instance is created once by <see cref="TwoDimensionalFramePlanner"/> and refilled by each
/// <c>Plan</c> call, so a warm frame allocates nothing. The renderer records the per-camera passes
/// from <see cref="Cameras"/> and <see cref="Runs"/> immediately after planning and before the next
/// plan overwrites them.
/// </para>
/// <para>
/// <see cref="CameraCount"/> is the snapshot's captured camera count, including cameras that were
/// invalid and skipped; <see cref="CameraPlanCount"/> is the valid subset.
/// <see cref="DrawCalls"/> is the total number of draw runs: every sprite run and every font run
/// records one <c>vkCmdDraw</c> and every line run one <c>vkCmdDrawIndexed</c>, so a camera with at
/// least one run records one draw per contiguous kind/shader/font change.
/// <see cref="NonEmptyCameras"/> is the number of cameras with at least one run, so
/// <c>NonEmptyCameras &lt;= DrawCalls</c>.
/// </para>
/// <para>
/// <see cref="InstanceCount"/> counts sprite instance records only, matching the public
/// <c>RenderStats.Instances</c> contract; the shared instance buffer also holds
/// <see cref="GlyphCount"/> glyph records, and each camera's
/// <see cref="CameraDrawPlan.InstanceCount"/> covers both. <see cref="FontCount"/> is the number of
/// captured font records, including skipped and empty ones.
/// </para>
/// </remarks>
internal sealed class TwoDimensionalFramePlan
{
    /// <summary>Gets the number of cameras captured by the snapshot, including invalid ones.</summary>
    internal int CameraCount { get; set; }

    /// <summary>Gets the number of cameras with a valid projection (the pass candidates).</summary>
    internal int CameraPlanCount { get; set; }

    /// <summary>Gets the number of draw runs, and therefore the number of 2D draw calls.</summary>
    internal int DrawCalls { get; set; }

    /// <summary>Gets the number of cameras that draw at least one record.</summary>
    internal int NonEmptyCameras { get; set; }

    /// <summary>Gets the total number of sprite instance records written into the frame buffer.</summary>
    internal int InstanceCount { get; set; }

    /// <summary>Gets the total number of glyph instance records written into the shared frame buffer.</summary>
    internal int GlyphCount { get; set; }

    /// <summary>Gets the number of captured font records, including skipped and empty ones.</summary>
    internal int FontCount { get; set; }

    /// <summary>Gets the total number of line records submitted by the frame across cameras.</summary>
    internal int LineCount { get; set; }

    /// <summary>Gets the total number of line vertices written into the frame buffer.</summary>
    internal int LineVertexCount { get; set; }

    /// <summary>Gets the total number of line indices written into the frame buffer.</summary>
    internal int LineIndexCount { get; set; }

    /// <summary>Gets the number of draw runs written into <see cref="Runs"/>.</summary>
    internal int RunCount { get; set; }

    /// <summary>Gets the number of sprites skipped by the frame (null/layer/non-Quad/pending).</summary>
    internal long SkippedSprites { get; set; }

    /// <summary>
    /// Gets the number of fonts skipped by the frame (null data, invalid layer, still-loading custom
    /// shader, or still-loading atlas page). An empty layout is a normal no-draw, not a skip.
    /// </summary>
    internal long SkippedFonts { get; set; }

    /// <summary>
    /// Gets the number of lines skipped by the frame (null geometry, invalid layer, or a
    /// still-loading custom shader).
    /// </summary>
    internal long SkippedLines { get; set; }

    /// <summary>
    /// Gets the number of sprites whose world bounds keep every layer-matching planned camera from
    /// emitting them.
    /// </summary>
    /// <remarks>
    /// A record is counted once per frame, not once per camera: a sprite that one camera culls but
    /// another draws is visible and is not counted. Records with empty (unknown) bounds are never
    /// culled and are never counted here.
    /// </remarks>
    internal int CulledSprites { get; set; }

    /// <summary>
    /// Gets the number of fonts whose world bounds keep every layer-matching planned camera from
    /// emitting any of their glyphs, with the same frame-level accounting as
    /// <see cref="CulledSprites"/>.
    /// </summary>
    internal int CulledFonts { get; set; }

    /// <summary>
    /// Gets the number of lines whose world bounds keep every layer-matching planned camera from
    /// emitting them, with the same frame-level accounting as <see cref="CulledSprites"/>.
    /// </summary>
    internal int CulledLines { get; set; }

    /// <summary>Gets the valid per-camera plans, in ascending <c>ZIndex</c> order.</summary>
    internal CameraDrawPlan[] Cameras { get; set; } = [];

    /// <summary>
    /// Gets the per-camera draw runs, in camera order and then in draw order; only the first
    /// <see cref="RunCount"/> entries are valid.
    /// </summary>
    internal DrawRun[] Runs { get; set; } = [];

    /// <summary>
    /// Clears the frame's counts, cameras, and runs so a pass that is not planned again records
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The instance is reused: <see cref="TwoDimensionalFramePlanner"/> refills it in place on the
    /// next plan, and the cleared array references are the shared empty array, so a reset never
    /// allocates.
    /// </remarks>
    internal void Reset()
    {
        CameraCount = 0;
        CameraPlanCount = 0;
        DrawCalls = 0;
        NonEmptyCameras = 0;
        InstanceCount = 0;
        GlyphCount = 0;
        FontCount = 0;
        LineCount = 0;
        LineVertexCount = 0;
        LineIndexCount = 0;
        RunCount = 0;
        SkippedSprites = 0;
        SkippedFonts = 0;
        SkippedLines = 0;
        CulledSprites = 0;
        CulledFonts = 0;
        CulledLines = 0;
        Cameras = [];
        Runs = [];
    }
}
