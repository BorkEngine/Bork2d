using System.Numerics;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

public sealed class SceneRootTests
{
    [Fact]
    public void RootIsAnIdentityTransformWithIdZero()
    {
        var engine = TestEngineFactory.Create();

        var root = Assert.IsType<SceneRoot>(engine.Root);

        Assert.Equal(0u, root.Id);
        Assert.Null(root.Parent);
        Assert.Equal(Vector2.Zero, root.LocalPosition);
        Assert.Equal(0f, root.LocalRotation);
        Assert.Equal(Vector2.One, root.LocalScale);
        Assert.Empty(root.GetRootedTransformsInIdOrder());
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
        Assert.Empty(Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder());

        var third = engine.CreateSpriteRender(engine.Root);

        Assert.Same(engine.Root, third.Parent);
        var rooted = Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder();
        Assert.Single(rooted);
        Assert.Same(third, rooted[0]);
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
