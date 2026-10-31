using System.Numerics;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

public sealed class SceneRootTests
{
    [Fact]
    public void RootIsNotATransform()
    {
        var engine = TestEngineFactory.Create();

        Assert.IsType<SceneRoot>(engine.Root);
        Assert.Equal(typeof(object), typeof(SceneRoot).BaseType);
        Assert.False(typeof(Transform).IsAssignableFrom(typeof(SceneRoot)));
        Assert.Contains(typeof(ISceneRoot), typeof(SceneRoot).GetInterfaces());
        Assert.DoesNotContain(typeof(Transform), typeof(SceneRoot).GetInterfaces());
    }

    [Fact]
    public void RootIsAnIdentityTransform()
    {
        var engine = TestEngineFactory.Create();

        var root = Assert.IsType<SceneRoot>(engine.Root);

        Assert.Null(root.Parent);
        Assert.Equal(Vector2.Zero, root.LocalPosition);
        Assert.Equal(0f, root.LocalRotation);
        Assert.Equal(Vector2.One, root.LocalScale);
    }

    [Fact]
    public void RootSettersThrowAndLeaveTheRootUnchanged()
    {
        var engine = TestEngineFactory.Create();
        var root = engine.Root;
        var child = engine.CreateTransform();

        Assert.Throws<InvalidOperationException>(() => { root.Parent = child; });
        Assert.Throws<InvalidOperationException>(() => { root.Parent = null; });
        Assert.Throws<InvalidOperationException>(() => { root.LocalPosition = new Vector2(1f, 2f); });
        Assert.Throws<InvalidOperationException>(() => { root.LocalRotation = 1f; });
        Assert.Throws<InvalidOperationException>(() => { root.LocalScale = new Vector2(2f, 2f); });

        Assert.Null(root.Parent);
        Assert.Equal(Vector2.Zero, root.LocalPosition);
        Assert.Equal(0f, root.LocalRotation);
        Assert.Equal(Vector2.One, root.LocalScale);
    }

    [Fact]
    public void RootRemainsValidAndCanOwnChildrenAfterDetachAll()
    {
        var engine = TestEngineFactory.Create();
        var first = engine.CreateTransform(engine.Root);
        var second = engine.CreateCamera(engine.Root);

        engine.Root.DetachAll();

        Assert.Null(first.Parent);
        Assert.Null(second.Parent);

        var third = engine.CreateSpriteRender(engine.Root);

        Assert.Same(engine.Root, third.Parent);
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
    }

    [Fact]
    public void RootCannotBeParented()
    {
        var engine = TestEngineFactory.Create();
        var transform = engine.CreateTransform(engine.Root);

        Assert.Throws<InvalidOperationException>(() => { engine.Root.Parent = transform; });
        Assert.Null(engine.Root.Parent);
    }
}
