using System.Numerics;
using Bork2d.Api;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

public sealed class TransformHierarchyTests
{
    [Fact]
    public void ReparentingPreservesWorldPositionRotationAndScale()
    {
        var engine = TestEngineFactory.Create();

        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(5f, -2f);
        first.LocalRotation = 0.75f;
        first.LocalScale = new Vector2(2f, 3f);

        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(-1f, 4f);
        second.LocalRotation = -0.5f;
        second.LocalScale = new Vector2(1.5f, 0.5f);

        var child = engine.CreateTransform(first);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 0.25f;
        child.LocalScale = new Vector2(0.5f, 2f);

        var worldMatrix = ((Transform)child).WorldMatrix();
        var worldPosition = ((Transform)child).WorldPosition();
        var worldRotation = ((Transform)child).WorldRotation();
        var worldScale = ((Transform)child).WorldScale();

        child.Parent = second;

        Assert.Same(second, child.Parent);
        TestAssertions.Close(worldMatrix, ((Transform)child).WorldMatrix());
        TestAssertions.Close(worldPosition, ((Transform)child).WorldPosition());
        TestAssertions.Close(worldRotation, ((Transform)child).WorldRotation());
        TestAssertions.Close(worldScale, ((Transform)child).WorldScale());

        var rooted = Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder();
        Assert.Contains(rooted, transform => ReferenceEquals(transform, child));
        Assert.Equal(3, rooted.Count);
    }

    [Fact]
    public void ReparentingPreservesTheComposedWorldMatrixThroughANestedHierarchy()
    {
        var engine = TestEngineFactory.Create();

        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(2f, -3f);
        first.LocalRotation = 0.9f;
        first.LocalScale = new Vector2(3f, 0.5f);

        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(-4f, 1.5f);
        second.LocalRotation = -0.35f;
        second.LocalScale = new Vector2(0.75f, 2.5f);

        var middle = engine.CreateTransform(first);
        middle.LocalPosition = new Vector2(1.5f, 0.5f);
        middle.LocalRotation = 0.4f;
        middle.LocalScale = new Vector2(2f, 0.75f);

        var child = engine.CreateTransform(middle);
        child.LocalPosition = new Vector2(-0.5f, 2f);
        child.LocalRotation = 0.65f;
        child.LocalScale = new Vector2(1.25f, 0.5f);

        var worldMatrix = ((Transform)child).WorldMatrix();
        var worldPosition = ((Transform)child).WorldPosition();
        var worldRotation = ((Transform)child).WorldRotation();
        var worldScale = ((Transform)child).WorldScale();

        child.Parent = second;

        Assert.Same(second, child.Parent);
        TestAssertions.Close(worldMatrix, ((Transform)child).WorldMatrix());
        TestAssertions.Close(worldPosition, ((Transform)child).WorldPosition());
        TestAssertions.Close(worldRotation, ((Transform)child).WorldRotation());
        TestAssertions.Close(worldScale, ((Transform)child).WorldScale());
    }

    [Fact]
    public void DetachingKeepsLocalValues()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(4f, 1f);
        parent.LocalRotation = 0.4f;
        parent.LocalScale = new Vector2(2f, 2f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 0.3f;
        child.LocalScale = new Vector2(0.5f, 0.25f);

        child.Parent = null;

        Assert.Null(child.Parent);
        TestAssertions.Close(new Vector2(1f, 2f), child.LocalPosition);
        TestAssertions.Close(0.3f, child.LocalRotation);
        TestAssertions.Close(new Vector2(0.5f, 0.25f), child.LocalScale);
    }

    [Fact]
    public void SettingLocalPositionPreservesTheRestOfTheLocalMatrix()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(1f, 2f);
        parent.LocalRotation = 0.75f;
        parent.LocalScale = new Vector2(2f, 3f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 0.4f;
        child.LocalScale = new Vector2(0.5f, 2f);

        var world = ((Transform)child).WorldMatrix();

        child.LocalPosition = new Vector2(-3f, 6f);
        var moved = ((Transform)child).WorldMatrix();

        TestAssertions.Close(world.M11, moved.M11);
        TestAssertions.Close(world.M12, moved.M12);
        TestAssertions.Close(world.M21, moved.M21);
        TestAssertions.Close(world.M22, moved.M22);
        Assert.NotEqual(world.Translation, moved.Translation);
        TestAssertions.Close(new Vector2(-3f, 6f), child.LocalPosition);
    }

    [Fact]
    public void AssigningTheSameParentIsANoOp()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(7f, -3f);
        parent.LocalRotation = 0.6f;
        parent.LocalScale = new Vector2(2f, 0.5f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 0.25f;
        child.LocalScale = new Vector2(0.5f, 2f);
        var worldMatrix = ((Transform)child).WorldMatrix();

        child.Parent = parent;

        Assert.Same(parent, child.Parent);
        TestAssertions.Close(new Vector2(1f, 2f), child.LocalPosition);
        TestAssertions.Close(0.25f, child.LocalRotation);
        TestAssertions.Close(new Vector2(0.5f, 2f), child.LocalScale);
        TestAssertions.Close(worldMatrix, ((Transform)child).WorldMatrix());
    }

    [Fact]
    public void SelfParentingAndCyclesAreRejected()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var second = engine.CreateTransform(first);
        var third = engine.CreateTransform(second);

        Assert.Throws<InvalidOperationException>(() => { first.Parent = first; });
        Assert.Throws<InvalidOperationException>(() => { first.Parent = second; });
        Assert.Throws<InvalidOperationException>(() => { first.Parent = third; });
        Assert.Throws<InvalidOperationException>(() => { engine.Root.Parent = first; });

        Assert.Same(engine.Root, first.Parent);
        Assert.Same(first, second.Parent);
        Assert.Same(second, third.Parent);
    }

    [Fact]
    public void ForeignTransformsCannotBeParents()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);
        var fake = new FakeTransform();

        Assert.Throws<Bork2dException>(() => { transform.Parent = fake; });
        Assert.Throws<Bork2dException>(() => { engine.CreateTransform(fake); });
        Assert.Throws<Bork2dException>(() => { engine.CreateCamera(fake); });
        Assert.Throws<Bork2dException>(() => { engine.CreateSpriteRender(fake); });
        Assert.Same(engine.Root, transform.Parent);
    }

    [Fact]
    public void ParentsFromAnotherSceneGraphAreRejected()
    {
        var engine = TestEngineFactory.Create();
        var otherEngine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);
        var foreign = otherEngine.CreateTransform(otherEngine.Root);

        Assert.Throws<Bork2dException>(() => { transform.Parent = foreign; });
        Assert.Throws<Bork2dException>(() => { engine.CreateTransform(foreign); });
        Assert.Same(engine.Root, transform.Parent);
    }
}
