using System.Numerics;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Tests for the slot-owned frame snapshot pool: counts, reset, high-water capacity, and no-shrink
/// (Q6=Z1).
/// </summary>
public sealed class FrameSnapshotTests
{
    [Fact]
    public void NewSnapshotStartsEmpty()
    {
        var snapshot = new FrameSnapshot();

        Assert.Equal(0, snapshot.CameraCount);
        Assert.Equal(0, snapshot.SpriteCount);
        Assert.Equal(0, snapshot.CameraCapacity);
        Assert.Equal(0, snapshot.SpriteCapacity);
        Assert.Equal(0, snapshot.Sequence);
        Assert.Equal(0d, snapshot.Time);
        Assert.Equal(0d, snapshot.DeltaTime);
        Assert.True(snapshot.Cameras.IsEmpty);
        Assert.True(snapshot.Sprites.IsEmpty);
    }

    [Fact]
    public void ResetClearsTheCountsAndScalarsButKeepsTheBuffers()
    {
        var snapshot = new FrameSnapshot();
        snapshot.AddCamera(Camera());
        snapshot.AddSprite(Sprite());
        snapshot.Sequence = 12;
        snapshot.Time = 1.5;
        snapshot.DeltaTime = 0.25;
        var cameraCapacity = snapshot.CameraCapacity;
        var spriteCapacity = snapshot.SpriteCapacity;

        snapshot.Reset();

        Assert.Equal(0, snapshot.CameraCount);
        Assert.Equal(0, snapshot.SpriteCount);
        Assert.Equal(0, snapshot.Sequence);
        Assert.Equal(0d, snapshot.Time);
        Assert.Equal(0d, snapshot.DeltaTime);
        Assert.Equal(cameraCapacity, snapshot.CameraCapacity);
        Assert.Equal(spriteCapacity, snapshot.SpriteCapacity);
    }

    [Fact]
    public void AddingRenderablesGrowsTheBuffersToTheHighWaterMark()
    {
        var snapshot = new FrameSnapshot();

        snapshot.AddSprite(Sprite());
        Assert.True(snapshot.SpriteCapacity >= 1);
        Assert.Equal(1, snapshot.SpriteCount);
        Assert.Equal(0, snapshot.CameraCapacity);

        snapshot.AddCamera(Camera());
        Assert.True(snapshot.CameraCapacity >= 1);
        Assert.Equal(1, snapshot.CameraCount);

        var capacity = snapshot.SpriteCapacity;
        for (var index = 0; index < 64; index++)
        {
            snapshot.AddSprite(Sprite());
        }

        Assert.True(snapshot.SpriteCapacity >= 65);
        Assert.True(snapshot.SpriteCapacity > capacity);
        Assert.Equal(65, snapshot.SpriteCount);
    }

    [Fact]
    public void BuffersNeverShrinkAfterTheHighWaterMark()
    {
        var snapshot = new FrameSnapshot();
        for (var index = 0; index < 8; index++)
        {
            snapshot.AddSprite(Sprite());
            snapshot.AddCamera(Camera());
        }

        var cameraCapacity = snapshot.CameraCapacity;
        var spriteCapacity = snapshot.SpriteCapacity;

        snapshot.Reset();
        snapshot.AddSprite(Sprite());
        snapshot.AddCamera(Camera());

        Assert.Equal(cameraCapacity, snapshot.CameraCapacity);
        Assert.Equal(spriteCapacity, snapshot.SpriteCapacity);
    }

    [Fact]
    public void SpansExposeOnlyTheAddedEntriesInCaptureOrder()
    {
        var snapshot = new FrameSnapshot();
        snapshot.AddCamera(Camera(zIndex: 5));
        snapshot.AddCamera(Camera(zIndex: -1));
        snapshot.AddSprite(Sprite(zIndex: 9));

        Assert.Equal(2, snapshot.Cameras.Length);
        Assert.Equal(5, snapshot.Cameras[0].ZIndex);
        Assert.Equal(-1, snapshot.Cameras[1].ZIndex);
        Assert.Equal(1, snapshot.Sprites.Length);
        Assert.Equal(9, snapshot.Sprites[0].ZIndex);
    }

    private static CameraSnapshot Camera(int zIndex = 0)
        => new(zIndex, Matrix3x2.Identity, 1f, new Vector4(0f, 0f, 1f, 1f), 1u, ClearKind.Color, Color.Black);

    private static SpriteSnapshot Sprite(int zIndex = 0)
        => new(null, Matrix3x2.Identity, Color.White, 1u, zIndex, FlipX: false, FlipY: false);
}
