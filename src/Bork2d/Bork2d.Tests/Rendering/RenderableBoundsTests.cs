using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Scene;
using Bork2d.Runtime.Window;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Tests for the read-only world bounds of cameras and sprites: identity/translated/rotated camera
/// projections, the offscreen and headless windows, sprite pivots/flips/rotations/shear, and the
/// empty-bounds no-throw rule for invalid renderables.
/// </summary>
public sealed class RenderableBoundsTests
{
    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    [Fact]
    public void CameraBoundsCoverTheVisibleRectangle()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;

        float halfWidth = 5f * (800f / 600f);
        AssertBounds(camera.Bounds, new Vector2(-halfWidth, -5f), new Vector2(halfWidth, 5f));
    }

    [Fact]
    public void CameraBoundsFollowTheCameraTransform()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.LocalPosition = new Vector2(10f, 20f);

        float halfWidth = 5f * (800f / 600f);
        AssertBounds(
            camera.Bounds,
            new Vector2(10f - halfWidth, 15f),
            new Vector2(10f + halfWidth, 25f));
    }

    [Fact]
    public void CameraBoundsAreTheAabbOfTheRotatedVisibleRectangle()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.LocalRotation = 90f;

        float halfWidth = 5f * (800f / 600f);
        AssertBounds(camera.Bounds, new Vector2(-5f, -halfWidth), new Vector2(5f, halfWidth));
    }

    [Fact]
    public void CameraBoundsUseTheRoundedViewportAspect()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.Viewport = new Vector4(0.5f, 0f, 0.5f, 1f);

        float halfWidth = 5f * (400f / 600f);
        AssertBounds(camera.Bounds, new Vector2(-halfWidth, -5f), new Vector2(halfWidth, 5f));
    }

    [Fact]
    public void OffscreenWindowProvidesTheCameraBounds()
    {
        var engine = new GameEngine<GameLayer>(
            new FakeTime(),
            new OffscreenWindow(800, 600, "offscreen"),
            new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;

        float halfWidth = 5f * (800f / 600f);
        AssertBounds(camera.Bounds, new Vector2(-halfWidth, -5f), new Vector2(halfWidth, 5f));
    }

    [Fact]
    public void HeadlessAndDisposedWindowsReturnEmptyCameraBoundsWithoutThrowing()
    {
        var headless = new GameEngine<GameLayer>(
            new FakeTime(),
            new UnsupportedWindow(),
            new FakeAssets());
        var headlessCamera = headless.CreateCamera(headless.Root);

        Assert.Equal(Bounds.Empty, headlessCamera.Bounds);

        var disposed = new GameEngine<GameLayer>(
            new FakeTime(),
            new DisposedWindow(),
            new FakeAssets());
        var disposedCamera = disposed.CreateCamera(disposed.Root);

        Assert.Equal(Bounds.Empty, disposedCamera.Bounds);
    }

    [Fact]
    public void InvalidCamerasReturnEmptyBoundsWithoutThrowing()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var camera = engine.CreateCamera(engine.Root);

        camera.Size = 0f;
        Assert.Equal(Bounds.Empty, camera.Bounds);

        camera.Size = float.NaN;
        Assert.Equal(Bounds.Empty, camera.Bounds);

        camera.Size = 5f;
        camera.Viewport = new Vector4(0f, 0f, 0f, 1f);
        Assert.Equal(Bounds.Empty, camera.Bounds);

        camera.Viewport = new Vector4(0f, 0f, 1f, 1f);
        camera.LocalScale = new Vector2(2f, 2f);
        Assert.Equal(Bounds.Empty, camera.Bounds);

        camera.LocalScale = new Vector2(0f, 0f);
        Assert.Equal(Bounds.Empty, camera.Bounds);

        camera.LocalScale = Vector2.One;
        camera.Size = float.MaxValue;
        Assert.Equal(Bounds.Empty, camera.Bounds);
    }

    [Fact]
    public void CameraBoundsUseTheComposedWorldTransform()
    {
        (GameEngine<GameLayer> engine, _) = CreateEngine(new Vector2(800f, 600f));
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(100f, -50f);
        var camera = engine.CreateCamera(parent);
        camera.LocalPosition = new Vector2(-100f, 50f);
        camera.Size = 5f;

        float halfWidth = 5f * (800f / 600f);
        AssertBounds(camera.Bounds, new Vector2(-halfWidth, -5f), new Vector2(halfWidth, 5f));
    }

    [Fact]
    public void SpriteBoundsCoverTheFullQuad()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(engine, Asset(scale: new Vector2(10f)));

        AssertBounds(sprite.Bounds, new Vector2(-5f, -5f), new Vector2(5f, 5f));
    }

    [Fact]
    public void SpriteBoundsUseTheUvSpanAndPerAxisWorldScale()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(
            engine,
            Asset(
                uvs:
                [
                    new Vector2(0.25f, 0.1f),
                    new Vector2(0.75f, 0.1f),
                    new Vector2(0.75f, 0.9f),
                    new Vector2(0.25f, 0.9f),
                ],
                scale: new Vector2(10f)));

        AssertBounds(sprite.Bounds, new Vector2(-2.5f, -4f), new Vector2(2.5f, 4f));
    }

    [Fact]
    public void SpriteBoundsHonorThePivot()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(
            engine,
            Asset(scale: new Vector2(10f), pivot: Vector2.Zero));

        AssertBounds(sprite.Bounds, new Vector2(0f, -10f), new Vector2(10f, 0f));
    }

    [Fact]
    public void SpriteBoundsHonorTheFlips()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> flipX = CreateSprite(
            engine,
            Asset(scale: new Vector2(10f), pivot: Vector2.Zero),
            flipX: true);
        ISpriteRender<GameLayer> flipY = CreateSprite(
            engine,
            Asset(scale: new Vector2(10f), pivot: Vector2.Zero),
            flipY: true);

        AssertBounds(flipX.Bounds, new Vector2(-10f, -10f), new Vector2(0f, 0f));
        AssertBounds(flipY.Bounds, new Vector2(0f, 0f), new Vector2(10f, 10f));
    }

    [Fact]
    public void SpriteBoundsFollowRotation()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(
            engine,
            Asset(scale: new Vector2(10f, 4f)));
        sprite.LocalPosition = new Vector2(3f, 4f);
        sprite.LocalRotation = 90f;

        AssertBounds(sprite.Bounds, new Vector2(1f, -1f), new Vector2(5f, 9f));
    }

    [Fact]
    public void SpriteBoundsAreConservativeForShearedTransforms()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalScale = new Vector2(2f, 1f);
        ISpriteRender<GameLayer> sprite = CreateSprite(
            engine,
            Asset(scale: new Vector2(10f, 4f)),
            parent: parent);
        sprite.LocalPosition = new Vector2(11f, 0f);
        sprite.LocalRotation = 30f;

        // world linear part = R(30) * S(2, 1); the quad half extents (5, 2) become
        // (5 * 2cos + 2 * 2sin, 5 * sin + 2 * cos) around the world center (22, 0).
        float cosine = MathF.Cos(MathF.PI / 6f);
        float sine = MathF.Sin(MathF.PI / 6f);
        float halfX = (5f * 2f * cosine) + (2f * 2f * sine);
        float halfY = (5f * sine) + (2f * cosine);
        AssertBounds(
            sprite.Bounds,
            new Vector2(22f - halfX, -halfY),
            new Vector2(22f + halfX, halfY));
    }

    [Fact]
    public void NullSpriteHasEmptyBounds()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);

        Assert.Equal(Bounds.Empty, sprite.Bounds);
    }

    [Theory]
    [MemberData(nameof(MalformedAssets))]
    public void MalformedSpritesHaveEmptyBoundsWithoutThrowing(SpriteAsset asset)
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(engine, asset);

        Assert.Equal(Bounds.Empty, sprite.Bounds);
    }

    [Fact]
    public void DegenerateWorldMatrixHasEmptyBoundsWithoutThrowing()
    {
        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        ISpriteRender<GameLayer> sprite = CreateSprite(engine, Asset(scale: new Vector2(10f)));
        sprite.LocalPosition = new Vector2(5f, 7f);
        sprite.LocalScale = Vector2.Zero;

        Assert.Equal(Bounds.Empty, sprite.Bounds);
    }

    [Fact]
    public void RenderablesAreAssignableToTheSharedContract()
    {
        Assert.True(typeof(IRenderable).IsAssignableFrom(typeof(ICamera<GameLayer>)));
        Assert.True(typeof(IRenderable).IsAssignableFrom(typeof(ISpriteRender<GameLayer>)));
        Assert.True(typeof(ITransform).IsAssignableFrom(typeof(IRenderable)));

        GameEngine<GameLayer> engine = CreateEngine(new FakeWindow());
        Assert.IsAssignableFrom<IRenderable>(engine.CreateCamera(engine.Root));
        Assert.IsAssignableFrom<IRenderable>(engine.CreateSpriteRender(engine.Root));
    }

    public static TheoryData<SpriteAsset> MalformedAssets()
    {
        SpriteAsset nullUvs = Asset();
        nullUvs.Uvs = null;
        SpriteAsset shortUvs = Asset();
        shortUvs.Uvs = [new Vector2(0f, 0f), new Vector2(1f, 1f)];
        SpriteAsset nonFiniteUv = Asset();
        nonFiniteUv.Uvs =
        [
            new Vector2(0f, 0f),
            new Vector2(float.NaN, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        ];
        SpriteAsset zeroScale = Asset(scale: Vector2.Zero);
        SpriteAsset negativeScale = Asset(scale: new Vector2(-1f, 1f));
        SpriteAsset nonFiniteScale = Asset(scale: new Vector2(1f, float.PositiveInfinity));
        SpriteAsset outsidePivot = Asset(pivot: new Vector2(-0.1f, 0.5f));
        SpriteAsset nonFinitePivot = Asset(pivot: new Vector2(0.5f, float.NaN));
        SpriteAsset nonQuad = Asset(topology: SpriteTopology.TriangleStrip);
        SpriteAsset unknownTopology = Asset(topology: (SpriteTopology)99);
        SpriteAsset indexedQuad = Asset(indices: [0, 1, 2]);

        return new TheoryData<SpriteAsset>
        {
            nullUvs,
            shortUvs,
            nonFiniteUv,
            zeroScale,
            negativeScale,
            nonFiniteScale,
            outsidePivot,
            nonFinitePivot,
            nonQuad,
            unknownTopology,
            indexedQuad,
        };
    }

    private static (GameEngine<GameLayer> Engine, FakeWindow Window) CreateEngine(Vector2 size)
    {
        var window = new FakeWindow { Size = size };
        return (new GameEngine<GameLayer>(new FakeTime(), window, new FakeAssets()), window);
    }

    private static GameEngine<GameLayer> CreateEngine(FakeWindow window)
        => new(new FakeTime(), window, new FakeAssets());

    private static ISpriteRender<GameLayer> CreateSprite(
        GameEngine<GameLayer> engine,
        SpriteAsset asset,
        ITransform? parent = null,
        bool flipX = false,
        bool flipY = false)
    {
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(parent ?? engine.Root);
        sprite.Sprite = asset;
        sprite.Layer = GameLayer.World;
        sprite.FlipX = flipX;
        sprite.FlipY = flipY;
        return sprite;
    }

    private static SpriteAsset Asset(
        Vector2[]? uvs = null,
        Vector2? scale = null,
        Vector2? pivot = null,
        SpriteTopology topology = SpriteTopology.Quad,
        ushort[]? indices = null)
        => new()
        {
            Texture = "textures/player",
            Uvs = uvs ?? _fullUvs,
            Scale = scale ?? new Vector2(10f),
            Pivot = pivot ?? new Vector2(0.5f, 0.5f),
            Topology = topology,
            Indices = indices,
        };

    private static void AssertBounds(Bounds bounds, Vector2 min, Vector2 max)
    {
        Assert.False(bounds.IsEmpty);
        TestAssertions.Close(min, bounds.Min);
        TestAssertions.Close(max, bounds.Max);
    }
}
