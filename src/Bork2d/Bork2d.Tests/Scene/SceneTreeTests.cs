using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Unordered-tree behavior: parent relationships and subtree lifetime, without entity IDs,
/// ordered discovery, or root enumeration.
/// </summary>
public sealed class SceneTreeTests
{
    [Fact]
    public void NestedObjectsKeepTheirParentRelationships()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var nested = engine.CreateTransform(first);
        var camera = engine.CreateCamera(engine.Root);
        var standalone = engine.CreateTransform();
        var sprite = engine.CreateSpriteRender(nested);
        var last = engine.CreateTransform(engine.Root);

        Assert.Same(engine.Root, first.Parent);
        Assert.Same(first, nested.Parent);
        Assert.Same(engine.Root, camera.Parent);
        Assert.Null(standalone.Parent);
        Assert.Same(nested, sprite.Parent);
        Assert.Same(engine.Root, last.Parent);
    }

    [Fact]
    public void DetachedSubtreesKeepTheirInternalParentLinks()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        var child = engine.CreateTransform(parent);
        var grandchild = engine.CreateTransform(child);

        parent.Parent = null;

        Assert.Null(parent.Parent);
        Assert.Same(parent, child.Parent);
        Assert.Same(child, grandchild.Parent);

        parent.Parent = engine.Root;

        Assert.Same(engine.Root, parent.Parent);
        Assert.Same(parent, child.Parent);
        Assert.Same(child, grandchild.Parent);
    }

    [Fact]
    public void DetachingAnIntermediateNodeKeepsItsSubtreeIntact()
    {
        var engine = TestEngineFactory.Create();
        var rig = engine.CreateTransform(engine.Root);
        var turret = engine.CreateTransform(rig);
        var barrel = engine.CreateTransform(turret);
        var muzzle = engine.CreateSpriteRender(barrel);

        rig.DetachAll();

        Assert.Same(engine.Root, rig.Parent);
        Assert.Null(turret.Parent);
        Assert.Same(turret, barrel.Parent);
        Assert.Same(barrel, muzzle.Parent);

        turret.Parent = rig;

        Assert.Same(rig, turret.Parent);
        Assert.Same(turret, barrel.Parent);
        Assert.Same(barrel, muzzle.Parent);
    }
}
