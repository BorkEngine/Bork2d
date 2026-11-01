using System.Numerics;
using System.Reflection;
using Bork2d.Engine;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

public sealed class TransformHierarchyTests
{
    [Fact]
    public void ReparentingPreservesWorldPositionRotationAndScale()
    {
        var engine = TestEngineFactory.Create();

        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(5f, -2f);
        first.LocalRotation = 43f;
        first.LocalScale = new Vector2(2f, 3f);

        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(-1f, 4f);
        second.LocalRotation = -30f;
        second.LocalScale = new Vector2(1.5f, 0.5f);

        var child = engine.CreateTransform(first);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 15f;
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
    }

    [Fact]
    public void ReparentingPreservesTheComposedWorldMatrixThroughANestedHierarchy()
    {
        var engine = TestEngineFactory.Create();

        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(2f, -3f);
        first.LocalRotation = 52f;
        first.LocalScale = new Vector2(3f, 0.5f);

        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(-4f, 1.5f);
        second.LocalRotation = -20f;
        second.LocalScale = new Vector2(0.75f, 2.5f);

        var middle = engine.CreateTransform(first);
        middle.LocalPosition = new Vector2(1.5f, 0.5f);
        middle.LocalRotation = 23f;
        middle.LocalScale = new Vector2(2f, 0.75f);

        var child = engine.CreateTransform(middle);
        child.LocalPosition = new Vector2(-0.5f, 2f);
        child.LocalRotation = 37f;
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
    public void DetachingPreservesTheWorldTransform()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(4f, 1f);
        parent.LocalRotation = 23f;
        parent.LocalScale = new Vector2(2f, 3f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 17f;
        child.LocalScale = new Vector2(0.5f, 0.25f);

        var worldMatrix = ((Transform)child).WorldMatrix();
        var worldPosition = ((Transform)child).WorldPosition();
        var worldRotation = ((Transform)child).WorldRotation();
        var worldScale = ((Transform)child).WorldScale();

        child.Parent = null;

        Assert.Null(child.Parent);
        TestAssertions.Close(worldMatrix, ((Transform)child).WorldMatrix());
        TestAssertions.Close(worldPosition, ((Transform)child).WorldPosition());
        TestAssertions.Close(worldRotation, ((Transform)child).WorldRotation());
        TestAssertions.Close(worldScale, ((Transform)child).WorldScale());
        TestAssertions.Close(worldPosition, child.LocalPosition);
        TestAssertions.Close(worldRotation, child.LocalRotation);
        TestAssertions.Close(worldScale, child.LocalScale);
    }

    [Fact]
    public void DetachingFromASingularParentPreservesTheWorldTransform()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(3f, -2f);
        parent.LocalRotation = 30f;
        parent.LocalScale = new Vector2(0f, 2f);
        Assert.False(
            Matrix3x2.Invert(((Transform)parent).WorldMatrix(), out _),
            "The old parent's world matrix must be singular for this test.");

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 15f;
        child.LocalScale = new Vector2(0.5f, 3f);

        var worldMatrix = ((Transform)child).WorldMatrix();
        var worldPosition = ((Transform)child).WorldPosition();

        child.Parent = null;

        Assert.Null(child.Parent);
        TestAssertions.Close(worldMatrix, ((Transform)child).WorldMatrix());
        TestAssertions.Close(worldPosition, ((Transform)child).WorldPosition());
        TestAssertions.Close(worldPosition, child.LocalPosition);
    }

    [Fact]
    public void DetachAllPreservesEachDirectChildsWorldTransform()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(3f, -1f);
        parent.LocalRotation = 37f;
        parent.LocalScale = new Vector2(2f, 0.5f);

        var first = engine.CreateTransform(parent);
        first.LocalPosition = new Vector2(1f, 2f);
        first.LocalRotation = 15f;
        first.LocalScale = new Vector2(0.5f, 3f);

        var second = engine.CreateTransform(parent);
        second.LocalPosition = new Vector2(-2f, 0.5f);
        second.LocalRotation = 80f;
        second.LocalScale = new Vector2(1.5f, 0.25f);

        var grandchild = engine.CreateTransform(first);
        grandchild.LocalPosition = new Vector2(0.5f, -0.5f);
        grandchild.LocalRotation = 10f;
        grandchild.LocalScale = new Vector2(2f, 2f);

        var firstWorld = ((Transform)first).WorldMatrix();
        var secondWorld = ((Transform)second).WorldMatrix();
        var grandchildWorld = ((Transform)grandchild).WorldMatrix();

        parent.DetachAll();

        Assert.Same(engine.Root, parent.Parent);
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
        Assert.Same(first, grandchild.Parent);
        TestAssertions.Close(firstWorld, ((Transform)first).WorldMatrix());
        TestAssertions.Close(secondWorld, ((Transform)second).WorldMatrix());
        TestAssertions.Close(grandchildWorld, ((Transform)grandchild).WorldMatrix());
        TestAssertions.Close(firstWorld.Translation, first.LocalPosition);
        TestAssertions.Close(secondWorld.Translation, second.LocalPosition);
    }

    [Fact]
    public void ChildIndexTracksSwapRemovalAndIsResetByDetach()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        var first = engine.CreateTransform(parent);
        var middle = engine.CreateTransform(parent);
        var last = engine.CreateTransform(parent);

        Assert.Equal(0, ((Transform)first).ChildIndex);
        Assert.Equal(1, ((Transform)middle).ChildIndex);
        Assert.Equal(2, ((Transform)last).ChildIndex);

        middle.Parent = null;

        Assert.Null(middle.Parent);
        Assert.Equal(-1, ((Transform)middle).ChildIndex);
        Assert.Equal(0, ((Transform)first).ChildIndex);
        Assert.Equal(1, ((Transform)last).ChildIndex);

        first.Parent = null;

        Assert.Null(first.Parent);
        Assert.Equal(-1, ((Transform)first).ChildIndex);
        Assert.Equal(0, ((Transform)last).ChildIndex);

        parent.DetachAll();

        Assert.Null(last.Parent);
        Assert.Equal(-1, ((Transform)last).ChildIndex);
    }

    [Fact]
    public void SettingLocalPositionPreservesTheRestOfTheLocalMatrix()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(1f, 2f);
        parent.LocalRotation = 43f;
        parent.LocalScale = new Vector2(2f, 3f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 23f;
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
    public void AuthoredLocalScaleRoundTripsWhileRotationNormalizes()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);

        transform.LocalScale = new Vector2(-1f, 1f);
        Assert.Equal(new Vector2(-1f, 1f), transform.LocalScale);
        Assert.Equal(0f, transform.LocalRotation);

        transform.LocalScale = new Vector2(1f, -1f);
        Assert.Equal(new Vector2(1f, -1f), transform.LocalScale);
        Assert.Equal(0f, transform.LocalRotation);

        transform.LocalRotation = 400f;
        Assert.Equal(40f, transform.LocalRotation);
        Assert.Equal(new Vector2(1f, -1f), transform.LocalScale);

        transform.LocalRotation = -4f;
        Assert.Equal(356f, transform.LocalRotation);
        Assert.Equal(new Vector2(1f, -1f), transform.LocalScale);
    }

    [Fact]
    public void EditingRotationAfterANegativeXScaleKeepsTheMirrorAxis()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);

        transform.LocalScale = new Vector2(-1f, 1f);
        transform.LocalRotation = 30f;

        Assert.Equal(new Vector2(-1f, 1f), transform.LocalScale);
        Assert.Equal(30f, transform.LocalRotation);

        transform.LocalRotation = 70f;

        Assert.Equal(new Vector2(-1f, 1f), transform.LocalScale);
        Assert.Equal(70f, transform.LocalRotation);
    }

    [Fact]
    public void ReparentingKeepsTheAuthoredMirrorAxis()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        first.LocalPosition = new Vector2(5f, -2f);
        var second = engine.CreateTransform(engine.Root);
        second.LocalPosition = new Vector2(-1f, 4f);

        var negativeX = engine.CreateTransform(first);
        negativeX.LocalPosition = new Vector2(1.5f, 0.5f);
        negativeX.LocalScale = new Vector2(-1f, 1f);
        negativeX.LocalRotation = 30f;
        var negativeXWorld = ((Transform)negativeX).WorldMatrix();

        negativeX.Parent = second;

        TestAssertions.Close(negativeXWorld, ((Transform)negativeX).WorldMatrix());
        TestAssertions.Close(new Vector2(-1f, 1f), negativeX.LocalScale);
        TestAssertions.Close(30f, negativeX.LocalRotation);

        var negativeY = engine.CreateTransform(first);
        negativeY.LocalPosition = new Vector2(-2f, 1f);
        negativeY.LocalScale = new Vector2(1f, -1f);
        negativeY.LocalRotation = 15f;
        var negativeYWorld = ((Transform)negativeY).WorldMatrix();

        negativeY.Parent = second;

        TestAssertions.Close(negativeYWorld, ((Transform)negativeY).WorldMatrix());
        TestAssertions.Close(new Vector2(1f, -1f), negativeY.LocalScale);
        TestAssertions.Close(15f, negativeY.LocalRotation);
    }

    [Fact]
    public void AssigningTheSameParentIsANoOp()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(7f, -3f);
        parent.LocalRotation = 35f;
        parent.LocalScale = new Vector2(2f, 0.5f);

        var child = engine.CreateTransform(parent);
        child.LocalPosition = new Vector2(1f, 2f);
        child.LocalRotation = 15f;
        child.LocalScale = new Vector2(0.5f, 2f);
        var worldMatrix = ((Transform)child).WorldMatrix();

        child.Parent = parent;

        Assert.Same(parent, child.Parent);
        TestAssertions.Close(new Vector2(1f, 2f), child.LocalPosition);
        TestAssertions.Close(15f, child.LocalRotation);
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
    public void TransformLocalPropertiesAreNotVirtual()
    {
        AssertNonVirtualProperty(nameof(Transform.Parent));
        AssertNonVirtualProperty(nameof(Transform.LocalPosition));
        AssertNonVirtualProperty(nameof(Transform.LocalRotation));
        AssertNonVirtualProperty(nameof(Transform.LocalScale));
    }

    [Fact]
    public void ParentsFromAnotherEngineAreRejected()
    {
        var engine = TestEngineFactory.Create();
        var otherEngine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);
        var foreign = otherEngine.CreateTransform(otherEngine.Root);

        Assert.Throws<Bork2dException>(() => { transform.Parent = foreign; });
        Assert.Throws<Bork2dException>(() => { engine.CreateTransform(foreign); });
        Assert.Same(engine.Root, transform.Parent);
    }

    private static void AssertNonVirtualProperty(string name)
    {
        var property = typeof(Transform).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);

        // Interface implementations are emitted as virtual-final accessors in IL, so "non-virtual"
        // means "not overridable": IsVirtual without IsFinal is a real `virtual` property.
        AssertAccessorIsNotOverridable(property.GetMethod!, name, "getter");
        AssertAccessorIsNotOverridable(property.SetMethod!, name, "setter");
    }

    private static void AssertAccessorIsNotOverridable(
        MethodInfo accessor,
        string propertyName,
        string kind)
    {
        Assert.False(
            accessor.IsVirtual && !accessor.IsFinal,
            $"The '{propertyName}' {kind} must not be overridable.");
    }
}
