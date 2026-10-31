using System.Numerics;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Tests for the public sprite flip members (Q2=B1): defaults, round-trip, and independence from
/// the transform.
/// </summary>
public sealed class SpriteFlipTests
{
    [Fact]
    public void FlipsDefaultToFalse()
    {
        var engine = TestEngineFactory.Create();

        var sprite = engine.CreateSpriteRender(engine.Root);

        Assert.False(sprite.FlipX);
        Assert.False(sprite.FlipY);
    }

    [Fact]
    public void FlipsRoundTripIndependently()
    {
        var engine = TestEngineFactory.Create();
        var sprite = engine.CreateSpriteRender(engine.Root);

        sprite.FlipX = true;
        Assert.True(sprite.FlipX);
        Assert.False(sprite.FlipY);

        sprite.FlipY = true;
        Assert.True(sprite.FlipX);
        Assert.True(sprite.FlipY);

        sprite.FlipX = false;
        Assert.False(sprite.FlipX);
        Assert.True(sprite.FlipY);

        sprite.FlipY = false;
        Assert.False(sprite.FlipX);
        Assert.False(sprite.FlipY);
    }

    [Fact]
    public void FlipsDoNotChangeTheTransform()
    {
        var engine = TestEngineFactory.Create();
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.LocalPosition = new Vector2(3f, 4f);
        sprite.LocalRotation = 30f;
        sprite.LocalScale = new Vector2(2f, -3f);
        var expectedWorld = ((Transform)sprite).WorldMatrix();

        sprite.FlipX = true;
        sprite.FlipY = true;

        Assert.Equal(new Vector2(3f, 4f), sprite.LocalPosition);
        Assert.Equal(30f, sprite.LocalRotation);
        Assert.Equal(new Vector2(2f, -3f), sprite.LocalScale);
        TestAssertions.Close(expectedWorld, ((Transform)sprite).WorldMatrix());
    }

    [Fact]
    public void TransformChangesDoNotChangeTheFlips()
    {
        var engine = TestEngineFactory.Create();
        var sprite = engine.CreateSpriteRender(engine.Root);
        sprite.FlipX = true;
        sprite.FlipY = true;

        sprite.LocalPosition = new Vector2(-8f, 2f);
        sprite.LocalRotation = 180f;
        sprite.LocalScale = new Vector2(-1f, 1f);
        sprite.Parent = engine.CreateTransform(engine.Root);

        Assert.True(sprite.FlipX);
        Assert.True(sprite.FlipY);
    }

    [Fact]
    public void InterfaceExposesTheFlips()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);

        sprite.FlipX = true;
        sprite.FlipY = true;

        Assert.True(sprite.FlipX);
        Assert.True(sprite.FlipY);
    }
}
