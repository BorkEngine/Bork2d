using System.Numerics;
using Bork2d.Api;
using Bork2d.Common;
using Xunit;

namespace Bork2d.Tests;

public sealed class SceneCreationTests
{
    [Fact]
    public void CameraHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        var camera = engine.CreateCamera(engine.Root);

        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), camera.Viewport);
        Assert.Equal(10f, camera.Size);
        Assert.Equal(GameLayer.World | GameLayer.Player | GameLayer.Ui, camera.CullingMask);
        Assert.Equal(ClearKind.Color, camera.Clear);
        Assert.Equal(Color.Black, camera.ClearColor);
        Assert.Same(engine.Root, camera.Parent);
    }

    [Fact]
    public void SpriteRenderHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        var sprite = engine.CreateSpriteRender(engine.Root);

        Assert.Equal(Sprite.Empty, sprite.Sprite);
        Assert.Equal(GameLayer.None, sprite.Layer);
        Assert.Equal(Color.White, sprite.Tint);
        Assert.Equal(0, sprite.ZIndex);
        Assert.Same(engine.Root, sprite.Parent);
    }

    [Fact]
    public void CameraAndSpriteCreatorsRequireAParent()
    {
        var engine = TestEngineFactory.Create();

        Assert.Throws<ArgumentNullException>(() => { engine.CreateCamera(null!); });
        Assert.Throws<ArgumentNullException>(() => { engine.CreateSpriteRender(null!); });
    }

    [Fact]
    public void CreateTransformWithoutAParentIsStandalone()
    {
        var engine = TestEngineFactory.Create();

        var implicitParent = engine.CreateTransform();
        var explicitParent = engine.CreateTransform(null);

        Assert.Null(implicitParent.Parent);
        Assert.Null(explicitParent.Parent);
        Assert.Empty(Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder());
    }

    [Fact]
    public void CreatedObjectsAttachRelativeToTheirParent()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(10f, 5f);
        parent.LocalRotation = 0.5f;
        parent.LocalScale = new Vector2(2f, 2f);

        var child = engine.CreateTransform(parent);
        var sprite = engine.CreateSpriteRender(parent);
        var camera = engine.CreateCamera(parent);

        Assert.Same(parent, child.Parent);
        Assert.Equal(Vector2.Zero, child.LocalPosition);
        Assert.Equal(0f, child.LocalRotation);
        Assert.Equal(Vector2.One, child.LocalScale);
        Assert.Same(parent, sprite.Parent);
        Assert.Same(parent, camera.Parent);
        AssertClose(new Vector2(10f, 5f), ((Transform)child).WorldPosition());
    }

    [Fact]
    public void CreatedObjectsAttachToTheRoot()
    {
        var engine = TestEngineFactory.Create();

        var transform = engine.CreateTransform(engine.Root);
        var camera = engine.CreateCamera(engine.Root);
        var sprite = engine.CreateSpriteRender(engine.Root);

        Assert.Same(engine.Root, transform.Parent);
        Assert.Same(engine.Root, camera.Parent);
        Assert.Same(engine.Root, sprite.Parent);
        Assert.Equal(Vector2.Zero, transform.LocalPosition);
        Assert.Equal(Vector2.One, transform.LocalScale);
        Assert.Equal(3, Assert.IsType<SceneRoot>(engine.Root).GetRootedTransformsInIdOrder().Count);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.True(
            Vector2.Distance(expected, actual) <= 1e-4f,
            $"Expected world position {expected}, got {actual}.");
    }
}
