using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Diagnostics;

/// <summary>
/// Deterministic tests for the one-frame <see cref="RenderStats"/> mapping (M8 §6.9/§6.10): every
/// documented field maps from the aggregate render-pass statistics/page store, and the public
/// invariants (<c>NonEmptyCameras &lt;= DrawCalls</c>, <c>NonEmptyCameras &lt;= Cameras</c>) hold.
/// </summary>
public sealed class FrameRenderStatsTests
{
    [Fact]
    public void TheSnapshotMapsEveryAggregateAndStoreField()
    {
        var passStats = new RenderPassStats
        {
            CameraCount = 3,
            CameraPlanCount = 2,
            DrawCalls = 2,
            NonEmptyCameras = 2,
            InstanceCount = 7,
            GlyphCount = 11,
            LineCount = 5,
            SkippedSprites = 4,
            SkippedFonts = 9,
            SkippedLines = 6,
            CulledSprites = 5,
            CulledFonts = 10,
            CulledLines = 8,
        };

        RenderStats stats = FrameRenderStats.Create(
            frameSequence: 42,
            framebufferWidth: 1280,
            framebufferHeight: 720,
            passStats,
            texturePages: 3,
            texturePageCapacity: 256,
            textureUploadsPending: 2,
            textureUploadBytesPending: 8192,
            validationErrors: 1,
            validationWarnings: 2);

        Assert.Equal(42, stats.FrameSequence);
        Assert.Equal(1280, stats.FramebufferWidth);
        Assert.Equal(720, stats.FramebufferHeight);
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(7, stats.Instances);
        Assert.Equal(11, stats.Glyphs);
        Assert.Equal(5, stats.Lines);
        Assert.Equal(3, stats.Cameras);
        Assert.Equal(2, stats.NonEmptyCameras);
        Assert.Equal(3, stats.TexturePages);
        Assert.Equal(256, stats.TexturePageCapacity);
        Assert.Equal(2, stats.TextureUploadsPending);
        Assert.Equal(8192, stats.TextureUploadBytesPending);
        Assert.Equal(4, stats.SkippedSprites);
        Assert.Equal(9, stats.SkippedFonts);
        Assert.Equal(6, stats.SkippedLines);
        Assert.Equal(1, stats.ValidationErrors);
        Assert.Equal(2, stats.ValidationWarnings);
    }

    [Fact]
    public void DrawCallRelationshipsAlwaysHold()
    {
        var passStats = new RenderPassStats
        {
            CameraCount = 4,
            CameraPlanCount = 0,
            DrawCalls = 0,
            NonEmptyCameras = 0,
            InstanceCount = 0,
            SkippedSprites = 9,
            CulledSprites = 6,
        };

        RenderStats stats = FrameRenderStats.Create(
            frameSequence: 1,
            framebufferWidth: 320,
            framebufferHeight: 240,
            passStats,
            texturePages: 1,
            texturePageCapacity: 256,
            textureUploadsPending: 0,
            textureUploadBytesPending: 0,
            validationErrors: 0,
            validationWarnings: 0);

        // Every shader run records one draw and a drawing camera has at least one run, so the draw
        // count is at least the non-empty camera count (and may exceed the camera count when a
        // camera contains several contiguous shader groups).
        Assert.True(stats.NonEmptyCameras <= stats.DrawCalls);
        Assert.True(stats.NonEmptyCameras <= stats.Cameras);
        Assert.Equal(9, stats.SkippedSprites);
        Assert.Equal(0, stats.Lines);
        Assert.Equal(0, stats.SkippedLines);
    }
}
