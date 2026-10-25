using System.Numerics;
using System.Runtime.CompilerServices;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// Deterministic lifetime tests for root-owned scene entities.
/// </summary>
/// <remarks>
/// The collection disables parallelization so a forced collection cannot be perturbed by other
/// test classes while a weak reference is being observed. The tests assert parent relationships,
/// <see cref="SceneRoot.DetachAll"/>, reference identity, and GC behavior; no entity IDs or root
/// enumeration are involved.
/// </remarks>
[Collection(SceneGcCollection.Name)]
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
    public void RootedCameraPersistsUntilTheRootDetachesIt()
    {
        var engine = TestEngineFactory.Create();
        var reference = CreateRootedCamera(engine);

        ForceCollect();

        Assert.True(reference.IsAlive, "A rooted camera must survive losing the game's reference.");

        engine.Root.DetachAll();
        AssertCollected(reference);
    }

    [Fact]
    public void RootedSpritePersistsUntilTheRootDetachesIt()
    {
        var engine = TestEngineFactory.Create();
        var reference = CreateRootedSprite(engine);

        ForceCollect();

        Assert.True(reference.IsAlive, "A rooted sprite must survive losing the game's reference.");

        engine.Root.DetachAll();
        AssertCollected(reference);
    }

    [Fact]
    public void DetachedCameraIsCollectibleAndLeavesTheScene()
    {
        var engine = TestEngineFactory.Create();
        var reference = CreateDetachedCamera(engine);

        // The root is still alive. Collectibility proves the root no longer retains the camera.
        AssertCollected(reference);
    }

    [Fact]
    public void DetachedSpriteIsCollectibleAndLeavesTheScene()
    {
        var engine = TestEngineFactory.Create();
        var reference = CreateDetachedSprite(engine);

        AssertCollected(reference);
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

        engine.Root.DetachAll();

        Assert.Null(transform.Parent);
    }

    [Fact]
    public void RootDetachAllDetachesDirectChildrenAndKeepsReferencesValid()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var camera = engine.CreateCamera(engine.Root);
        var grandchild = engine.CreateTransform(first);
        var sprite = engine.CreateSpriteRender(first);

        engine.Root.DetachAll();

        Assert.Null(first.Parent);
        Assert.Null(camera.Parent);
        Assert.Same(first, grandchild.Parent);
        Assert.Same(first, sprite.Parent);

        first.Parent = engine.Root;

        Assert.Same(engine.Root, first.Parent);
        Assert.Same(first, grandchild.Parent);
        Assert.Same(first, sprite.Parent);

        // The camera was detached by DetachAll and is still detached; re-attach it explicitly.
        camera.Parent = engine.Root;
        Assert.Same(engine.Root, camera.Parent);

        engine.Root.DetachAll();

        Assert.Null(first.Parent);
        Assert.Null(camera.Parent);
        Assert.Same(first, grandchild.Parent);
    }

    [Fact]
    public void DetachAllWorksOnAnyTransform()
    {
        var engine = TestEngineFactory.Create();
        var rig = engine.CreateTransform(engine.Root);
        var turret = engine.CreateTransform(rig);
        var barrel = engine.CreateTransform(turret);

        rig.DetachAll();

        Assert.Same(engine.Root, rig.Parent);
        Assert.Null(turret.Parent);
        Assert.Same(turret, barrel.Parent);

        turret.Parent = rig;

        Assert.Same(rig, turret.Parent);
        Assert.Same(turret, barrel.Parent);
    }

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
    private static WeakReference CreateRootedCamera(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var camera = engine.CreateCamera(engine.Root);
        return new WeakReference(camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRootedSprite(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var sprite = engine.CreateSpriteRender(engine.Root);
        return new WeakReference(sprite);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDetachedCamera(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var camera = engine.CreateCamera(engine.Root);
        camera.Parent = null;
        return new WeakReference(camera);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDetachedSprite(Engine<GameLayer, TestGame<GameLayer>> engine)
    {
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Parent = null;
        return new WeakReference(sprite);
    }
}
