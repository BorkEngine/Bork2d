using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Api;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Deterministic lifetime tests for root-owned scene entities.
/// </summary>
/// <remarks>
/// The collection disables parallelization so a forced collection cannot be perturbed by other
/// test classes while a weak reference is being observed.
/// </remarks>
[Collection("Scene graph GC")]
public sealed class SceneLifetimeTests
{
    [Fact]
    public void UnparentedTransformIsCollectible()
    {
        var engine = TestEngineFactory.Create();

        var reference = CreateUnparentedTransform(engine);

        AssertCollected(reference);
    }

    [Fact]
    public void UnparentedCameraIsCollectible()
    {
        var engine = TestEngineFactory.Create();

        var reference = CreateUnparentedCamera(engine);

        AssertCollected(reference);
    }

    [Fact]
    public void UnparentedSpriteIsCollectible()
    {
        var engine = TestEngineFactory.Create();

        var reference = CreateUnparentedSprite(engine);

        AssertCollected(reference);
    }

    [Fact]
    public void RootedCameraPersistsAfterTheGameDropsItsReference()
    {
        var engine = TestEngineFactory.Create();
        var (reference, id) = CreateRootedCamera(engine);

        ForceCollect();

        Assert.True(reference.IsAlive, "A rooted camera must survive losing the game's reference.");
        var rooted = Root(engine).GetRootedTransformsInIdOrder();
        var camera = Assert.Single(rooted.OfType<Camera<GameLayer>>());
        Assert.Equal(id, camera.Id);
    }

    [Fact]
    public void RootedSpritePersistsAfterTheGameDropsItsReference()
    {
        var engine = TestEngineFactory.Create();
        var (reference, id) = CreateRootedSprite(engine);

        ForceCollect();

        Assert.True(reference.IsAlive, "A rooted sprite must survive losing the game's reference.");
        var rooted = Root(engine).GetRootedTransformsInIdOrder();
        var sprite = Assert.Single(rooted.OfType<SpriteRender<GameLayer>>());
        Assert.Equal(id, sprite.Id);
    }

    [Fact]
    public void DetachedCameraIsCollectibleAndLeavesTheScene()
    {
        var engine = TestEngineFactory.Create();
        var (reference, id) = CreateDetachedCamera(engine);

        AssertCollected(reference);

        Assert.DoesNotContain(Root(engine).GetRootedTransformsInIdOrder(), transform => transform.Id == id);
    }

    [Fact]
    public void DetachedSpriteIsCollectibleAndLeavesTheScene()
    {
        var engine = TestEngineFactory.Create();
        var (reference, id) = CreateDetachedSprite(engine);

        AssertCollected(reference);

        Assert.DoesNotContain(Root(engine).GetRootedTransformsInIdOrder(), transform => transform.Id == id);
    }

    [Fact]
    public void RemovingAndReAddingPreservesTheObjectAndItsState()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);
        transform.LocalPosition = new Vector2(3f, -4f);
        transform.LocalRotation = 30f;
        transform.LocalScale = new Vector2(2f, 3f);

        transform.Parent = null;

        Assert.Null(transform.Parent);
        TestAssertions.Close(new Vector2(3f, -4f), transform.LocalPosition);
        TestAssertions.Close(30f, transform.LocalRotation);
        TestAssertions.Close(new Vector2(2f, 3f), transform.LocalScale);

        transform.Parent = engine.Root;

        Assert.Same(engine.Root, transform.Parent);
        TestAssertions.Close(new Vector2(3f, -4f), transform.LocalPosition);
        TestAssertions.Close(30f, transform.LocalRotation);
        TestAssertions.Close(new Vector2(2f, 3f), transform.LocalScale);
        Assert.Contains(Root(engine).GetRootedTransformsInIdOrder(), rooted => ReferenceEquals(rooted, transform));
    }

    [Fact]
    public void RootDetachAllDetachesDirectChildrenAndKeepsReferencesValid()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var camera = engine.CreateCamera(engine.Root);
        var grandchild = engine.CreateTransform(first);
        var sprite = engine.CreateSpriteRender(first);
        var firstId = ((Transform)first).Id;

        engine.Root.DetachAll();

        Assert.Null(first.Parent);
        Assert.Null(camera.Parent);
        Assert.Same(first, grandchild.Parent);
        Assert.Same(first, sprite.Parent);
        Assert.Empty(Root(engine).GetRootedTransformsInIdOrder());

        first.Parent = engine.Root;

        Assert.Same(engine.Root, first.Parent);
        Assert.Equal(firstId, ((Transform)first).Id);
        var rooted = Root(engine).GetRootedTransformsInIdOrder();
        Assert.Equal(3, rooted.Count);
        Assert.Contains(rooted, transform => ReferenceEquals(transform, first));
        Assert.Contains(rooted, transform => ReferenceEquals(transform, grandchild));
        Assert.Contains(rooted, transform => ReferenceEquals(transform, sprite));

        engine.Root.DetachAll();

        Assert.Empty(Root(engine).GetRootedTransformsInIdOrder());
    }

    [Fact]
    public void DetachAllWorksOnAnyTransform()
    {
        var engine = TestEngineFactory.Create();
        var rig = engine.CreateTransform(engine.Root);
        var turret = engine.CreateTransform(rig);
        var barrel = engine.CreateTransform(turret);

        rig.DetachAll();

        Assert.Null(turret.Parent);
        Assert.Same(turret, barrel.Parent);
        var rooted = Root(engine).GetRootedTransformsInIdOrder();
        Assert.Contains(rooted, transform => ReferenceEquals(transform, rig));
        Assert.DoesNotContain(rooted, transform => ReferenceEquals(transform, turret));
        Assert.DoesNotContain(rooted, transform => ReferenceEquals(transform, barrel));

        turret.Parent = rig;

        Assert.Same(rig, turret.Parent);
        Assert.Same(turret, barrel.Parent);
        Assert.Contains(Root(engine).GetRootedTransformsInIdOrder(), transform => ReferenceEquals(transform, barrel));
    }

    private static SceneRoot Root(Engine<GameLayer, TestGame<GameLayer>> engine)
        => Assert.IsType<SceneRoot>(engine.Root);

    private static void ForceCollect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (var attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            ForceCollect();
        }

        Assert.False(
            reference.IsAlive,
            "An unparented or detached object must be collectible once the last reference is dropped.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnparentedTransform(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var transform = engine.CreateTransform();
        return new WeakReference(transform);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnparentedCamera(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var camera = engine.CreateCamera(engine.Root);
        camera.Parent = null;
        return new WeakReference(camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnparentedSprite(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Parent = null;
        return new WeakReference(sprite);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, uint Id) CreateRootedCamera(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var camera = engine.CreateCamera(engine.Root);
        return (new WeakReference(camera), ((Transform)camera).Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, uint Id) CreateRootedSprite(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var sprite = engine.CreateSpriteRender(engine.Root);
        return (new WeakReference(sprite), ((Transform)sprite).Id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, uint Id) CreateDetachedCamera(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var camera = engine.CreateCamera(engine.Root);
        var id = ((Transform)camera).Id;
        camera.Parent = null;
        return (new WeakReference(camera), id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, uint Id) CreateDetachedSprite(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var sprite = engine.CreateSpriteRender(engine.Root);
        var id = ((Transform)sprite).Id;
        sprite.Parent = null;
        return (new WeakReference(sprite), id);
    }
}
