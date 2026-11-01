using System.Runtime.CompilerServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Tests.Scene;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic GC regression for snapshot reuse: clearing a slot releases obsolete render data
/// and detached nodes without shrinking the pooled buffers.
/// </summary>
/// <remarks>
/// The collection disables parallelization so a forced collection cannot be perturbed by another
/// test class while the weak references are observed.
/// </remarks>
[Collection(SceneGcCollection.Name)]
public sealed class SnapshotCaptureLifetimeTests
{
    [Fact]
    public void ReusedSnapshotReleasesObsoleteSpriteStateAndKeepsBufferCapacity()
    {
        var snapshot = new FrameSnapshot();

        var (sprite, renderData) = CaptureSpriteThenClear(snapshot, out var capacity);

        AssertCollected(sprite, "The detached sprite must not be retained by the capture stack.");
        AssertCollected(renderData, "The frozen render data must not be retained by the sprite tail.");
        Assert.Equal(0, snapshot.SpriteCount);
        Assert.True(capacity >= 1, "The first capture must have populated the sprite buffer.");
        Assert.Equal(capacity, snapshot.SpriteCapacity);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Sprite, WeakReference RenderData) CaptureSpriteThenClear(
        FrameSnapshot snapshot,
        out int capacity)
    {
        var engine = TestEngineFactory.Create();
        var asset = new SpriteAsset { Texture = "textures/player" };
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Sprite = asset;

        engine.CaptureSnapshot(snapshot, sequence: 1);
        capacity = snapshot.SpriteCapacity;
        Assert.True(capacity >= 1, "The first capture must have populated the sprite buffer.");
        SpriteRenderData frozen = Assert.IsType<SpriteRenderData>(snapshot.Sprites[0].SpriteData);

        engine.Root.DetachAll();
        engine.CaptureSnapshot(snapshot, sequence: 2);
        Assert.Equal(0, snapshot.SpriteCount);

        return (new WeakReference(sprite), new WeakReference(frozen));
    }

    private static void ForceCollect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void AssertCollected(WeakReference reference, string because)
    {
        for (var attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            ForceCollect();
        }

        Assert.False(reference.IsAlive, because);
    }
}
