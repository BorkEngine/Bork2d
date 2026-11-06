using System.Numerics;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

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
        Assert.Equal(0, camera.ZIndex);
        Assert.Same(engine.Root, camera.Parent);
    }

    [Fact]
    public void CameraZIndexDefaultsToZeroAndRoundTrips()
    {
        var engine = TestEngineFactory.Create();
        var camera = engine.CreateCamera(engine.Root);

        Assert.Equal(0, camera.ZIndex);

        camera.ZIndex = 7;
        Assert.Equal(7, camera.ZIndex);

        camera.ZIndex = -3;
        Assert.Equal(-3, camera.ZIndex);

        camera.ZIndex = 0;
        Assert.Equal(0, camera.ZIndex);
    }

    [Fact]
    public void SpriteRenderHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        var sprite = engine.CreateSpriteRender(engine.Root);

        Assert.Null(sprite.Sprite);
        Assert.Equal(GameLayer.None, sprite.Layer);
        Assert.Equal(Color.White, sprite.Color);
        Assert.Equal(0, sprite.ZIndex);
        Assert.Same(engine.Root, sprite.Parent);
    }

    [Fact]
    public void LineRenderHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        var line = engine.CreateLineRender(engine.Root);

        Assert.Empty(line.Points);
        Assert.False(line.Closed);
        Assert.Equal(1f, line.Width);
        Assert.Equal(Color.White, line.Color);
        Assert.True(line.AntiAlias);
        Assert.Null(line.Shader);
        Assert.Equal(GameLayer.None, line.Layer);
        Assert.Equal(0, line.ZIndex);
        Assert.Same(engine.Root, line.Parent);
    }

    [Fact]
    public void FontRenderHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        var font = engine.CreateFontRender(engine.Root);

        Assert.Null(font.Font);
        Assert.Equal(string.Empty, font.Text);
        Assert.Equal(1f, font.Size);
        Assert.Equal(TextAlignment.Left, font.Alignment);
        Assert.Equal(1f, font.LineSpacing);
        Assert.Equal(Color.White, font.Color);
        Assert.Null(font.Shader);
        Assert.Equal(GameLayer.None, font.Layer);
        Assert.Equal(0, font.ZIndex);
        Assert.Same(engine.Root, font.Parent);
        Assert.Equal(Bounds.Empty, font.Bounds);
    }

    [Fact]
    public void RenderableCreatorsRequireAParent()
    {
        var engine = TestEngineFactory.Create();

        Assert.Throws<ArgumentNullException>(() => { engine.CreateCamera(null!); });
        Assert.Throws<ArgumentNullException>(() => { engine.CreateSpriteRender(null!); });
        Assert.Throws<ArgumentNullException>(() => { engine.CreateLineRender(null!); });
        Assert.Throws<ArgumentNullException>(() => { engine.CreateFontRender(null!); });
    }

    [Fact]
    public void CreateTransformWithoutAParentIsStandalone()
    {
        var engine = TestEngineFactory.Create();

        var implicitParent = engine.CreateTransform();
        var explicitParent = engine.CreateTransform(null);

        Assert.Null(implicitParent.Parent);
        Assert.Null(explicitParent.Parent);
    }

    [Fact]
    public void CreatedObjectsAttachRelativeToTheirParent()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(10f, 5f);
        parent.LocalRotation = 30f;
        parent.LocalScale = new Vector2(2f, 2f);

        var child = engine.CreateTransform(parent);
        var sprite = engine.CreateSpriteRender(parent);
        var camera = engine.CreateCamera(parent);
        var line = engine.CreateLineRender(parent);
        var font = engine.CreateFontRender(parent);

        Assert.Same(parent, child.Parent);
        Assert.Equal(Vector2.Zero, child.LocalPosition);
        Assert.Equal(0f, child.LocalRotation);
        Assert.Equal(Vector2.One, child.LocalScale);
        Assert.Same(parent, sprite.Parent);
        Assert.Same(parent, camera.Parent);
        Assert.Same(parent, line.Parent);
        Assert.Same(parent, font.Parent);
        TestAssertions.Close(new Vector2(10f, 5f), ((Transform)child).WorldPosition());
    }

    [Fact]
    public void CreatedObjectsAttachToTheRoot()
    {
        var engine = TestEngineFactory.Create();

        var transform = engine.CreateTransform(engine.Root);
        var camera = engine.CreateCamera(engine.Root);
        var sprite = engine.CreateSpriteRender(engine.Root);
        var line = engine.CreateLineRender(engine.Root);
        var font = engine.CreateFontRender(engine.Root);

        Assert.Same(engine.Root, transform.Parent);
        Assert.Same(engine.Root, camera.Parent);
        Assert.Same(engine.Root, sprite.Parent);
        Assert.Same(engine.Root, line.Parent);
        Assert.Same(engine.Root, font.Parent);
        Assert.Equal(Vector2.Zero, transform.LocalPosition);
        Assert.Equal(Vector2.One, transform.LocalScale);
    }
}
