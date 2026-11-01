using Bork2d.Engine.Scene;
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

    [Fact]
    public void LargeFlatPopulationSurvivesScatteredRemovalAndReattachment()
    {
        const int childCount = 2000;
        const int removeStride = 7;

        var engine = TestEngineFactory.Create();
        var newParent = engine.CreateTransform();
        var children = new ITransform[childCount];
        for (var index = 0; index < childCount; index++)
        {
            children[index] = engine.CreateTransform(engine.Root);
        }

        var removed = new List<int>();
        for (var index = 0; index < childCount; index += removeStride)
        {
            children[index].Parent = null;
            removed.Add(index);
        }

        var reattached = new HashSet<int>();
        for (var index = 0; index < removed.Count; index += 2)
        {
            children[removed[index]].Parent = newParent;
            reattached.Add(removed[index]);
        }

        var rootIndexes = new HashSet<int>();
        var newParentIndexes = new HashSet<int>();
        for (var index = 0; index < childCount; index++)
        {
            var childIndex = ((Transform)children[index]).ChildIndex;
            if (index % removeStride != 0)
            {
                Assert.Same(engine.Root, children[index].Parent);
                Assert.True(childIndex >= 0, $"Rooted child {index} must cache a valid index.");
                Assert.True(rootIndexes.Add(childIndex), $"Rooted child {index} has a duplicate index.");
            }
            else if (reattached.Contains(index))
            {
                Assert.Same(newParent, children[index].Parent);
                Assert.True(childIndex >= 0, $"Re-attached child {index} must cache a valid index.");
                Assert.True(
                    newParentIndexes.Add(childIndex),
                    $"Re-attached child {index} has a duplicate index.");
            }
            else
            {
                Assert.Null(children[index].Parent);
                Assert.Equal(-1, childIndex);
            }
        }

        Assert.Equal(childCount - removed.Count, rootIndexes.Count);
        Assert.Equal(reattached.Count, newParentIndexes.Count);
        AssertIndexesAreDense(rootIndexes);
        AssertIndexesAreDense(newParentIndexes);
    }

    private static void AssertIndexesAreDense(HashSet<int> indexes)
    {
        Assert.All(indexes, index => Assert.InRange(index, 0, indexes.Count - 1));
    }
}
