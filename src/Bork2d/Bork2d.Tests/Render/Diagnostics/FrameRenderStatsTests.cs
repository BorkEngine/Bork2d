using Bork2d.Engine.Rendering;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Diagnostics;

/// <summary>
/// Deterministic tests for the one-frame <see cref="RenderStats"/> mapping (M8 §6.9/§6.10): every
/// documented field maps from the aggregate render-pass statistics/page store, and the public
/// invariants (<c>DrawCalls == NonEmptyCameras &lt;= Cameras</c>) hold.
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
            SkippedSprites = 4,
            CulledSprites = 5,
        };

        RenderStats stats = FrameRenderStats.Create(
            frameSequence: 42,
            framebufferWidth: 1280,
            framebufferHeight: 720,
            passStats,
            texturePages: 3,
            texturePageCapacity: 256,
            validationErrors: 1,
            validationWarnings: 2);

        Assert.Equal(42, stats.FrameSequence);
        Assert.Equal(1280, stats.FramebufferWidth);
        Assert.Equal(720, stats.FramebufferHeight);
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(7, stats.Instances);
        Assert.Equal(3, stats.Cameras);
        Assert.Equal(2, stats.NonEmptyCameras);
        Assert.Equal(3, stats.TexturePages);
        Assert.Equal(256, stats.TexturePageCapacity);
        Assert.Equal(4, stats.SkippedSprites);
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
            validationErrors: 0,
            validationWarnings: 0);

        Assert.Equal(stats.NonEmptyCameras, stats.DrawCalls);
        Assert.True(stats.DrawCalls <= stats.Cameras);
        Assert.Equal(9, stats.SkippedSprites);
    }
}
