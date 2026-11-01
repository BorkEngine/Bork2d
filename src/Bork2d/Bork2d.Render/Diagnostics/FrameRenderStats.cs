using Bork2d.Engine.Rendering;
using Bork2d.Render.Vulkan;

namespace Bork2d.Render.Diagnostics;

/// <summary>
/// Builds the public <see cref="RenderStats"/> snapshot from one completed frame's aggregate
/// render-pass statistics and the page store state (M8 §6.9/§6.10).
/// </summary>
/// <remarks>
/// The mapping is pure and allocation-free so the renderer publishes it once per completed frame and
/// deterministic tests can assert the relationships without a GPU:
/// <c>DrawCalls == NonEmptyCameras &lt;= Cameras</c>, <c>Instances</c> is the aggregate total, and
/// <c>SkippedSprites</c> excludes placeholder-drawn sprites. Validation counters are cumulative for
/// the run; every other value describes the frame that was just submitted/presented.
/// </remarks>
internal static class FrameRenderStats
{
    /// <summary>Creates one frame's public stats snapshot.</summary>
    /// <param name="frameSequence">The snapshot sequence the frame consumed.</param>
    /// <param name="framebufferWidth">The target/framebuffer width in pixels for this frame.</param>
    /// <param name="framebufferHeight">The target/framebuffer height in pixels for this frame.</param>
    /// <param name="stats">The planned frame's aggregate render-pass statistics.</param>
    /// <param name="texturePages">
    /// The ready pages in the current catalog, including the placeholder; retired images are excluded.
    /// </param>
    /// <param name="texturePageCapacity">The current bindless page capacity, including the placeholder.</param>
    /// <param name="textureUploadsPending">
    /// The content-ready pages queued for upload but not begun by this frame's pump.
    /// </param>
    /// <param name="textureUploadBytesPending">
    /// The declared payload bytes of <paramref name="textureUploadsPending"/>.
    /// </param>
    /// <param name="validationErrors">The cumulative fatal validation errors for the run.</param>
    /// <param name="validationWarnings">The cumulative validation warnings for the run.</param>
    /// <returns>The immutable public snapshot.</returns>
    internal static RenderStats Create(
        long frameSequence,
        int framebufferWidth,
        int framebufferHeight,
        RenderPassStats stats,
        int texturePages,
        int texturePageCapacity,
        int textureUploadsPending,
        long textureUploadBytesPending,
        long validationErrors,
        long validationWarnings)
    {
        ArgumentNullException.ThrowIfNull(stats);

        return new RenderStats(
            FrameSequence: frameSequence,
            FramebufferWidth: framebufferWidth,
            FramebufferHeight: framebufferHeight,
            DrawCalls: stats.DrawCalls,
            Instances: stats.InstanceCount,
            Cameras: stats.CameraCount,
            NonEmptyCameras: stats.NonEmptyCameras,
            TexturePages: texturePages,
            TexturePageCapacity: texturePageCapacity,
            TextureUploadsPending: textureUploadsPending,
            TextureUploadBytesPending: textureUploadBytesPending,
            SkippedSprites: stats.SkippedSprites,
            ValidationErrors: validationErrors,
            ValidationWarnings: validationWarnings);
    }
}
