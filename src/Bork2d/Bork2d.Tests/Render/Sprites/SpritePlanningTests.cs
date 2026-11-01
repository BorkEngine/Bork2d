using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for sprite sort/filter/classification, quad geometry/flips, and the fatal
/// malformed-field policy (M8 §6.4/§6.8/§8.1).
/// </summary>
public sealed class SpritePlanningTests
{
    [Fact]
    public void SortOrdersByLayerThenZIndex()
    {
        SpriteSnapshot[] sprites =
        [
            Snapshot(layer: 4, zIndex: 0),
            Snapshot(layer: 1, zIndex: 3),
            Snapshot(layer: 1, zIndex: 1),
            Snapshot(layer: 2, zIndex: 9),
        ];
        var indices = new int[sprites.Length];

        SpritePlanning.Sort(sprites, indices);

        Assert.Equal(new[] { 2, 1, 3, 0 }, indices);
    }

    [Fact]
    public void SortDoesNotReorderTheSnapshot()
    {
        SpriteSnapshot[] sprites =
        [
            Snapshot(layer: 4, zIndex: 0),
            Snapshot(layer: 1, zIndex: 1),
        ];
        SpriteSnapshot before = sprites[0];
        var indices = new int[sprites.Length];

        SpritePlanning.Sort(sprites, indices);

        Assert.Equal(before, sprites[0]);
        Assert.Equal(new[] { 1, 0 }, indices);
    }

    [Fact]
    public void SortRejectsAShortIndexBuffer()
    {
        SpriteSnapshot[] sprites = [Snapshot(), Snapshot()];

        Assert.Throws<ArgumentException>(() => SpritePlanning.Sort(sprites, new int[1]));
    }

    [Fact]
    public void SortCamerasOrdersByZIndex()
    {
        CameraSnapshot[] cameras = [Camera(zIndex: 5), Camera(zIndex: 1), Camera(zIndex: 3)];
        var indices = new int[cameras.Length];

        SpritePlanning.SortCameras(cameras, indices);

        Assert.Equal(new[] { 1, 2, 0 }, indices);
    }

    [Fact]
    public void SortCamerasRejectsAShortIndexBuffer()
    {
        CameraSnapshot[] cameras = [Camera(zIndex: 0), Camera(zIndex: 1)];

        Assert.Throws<ArgumentException>(() => SpritePlanning.SortCameras(cameras, new int[1]));
    }

    [Fact]
    public void SharedIndexSorterOrdersBothSnapshotTypesByKeyOnly()
    {
        // Equal keys are explicitly unspecified, so this asserts the key sequence and a complete
        // permutation instead of relying on a stable tie order.
        SpriteSnapshot[] sprites =
        [
            Snapshot(layer: 2, zIndex: 7),
            Snapshot(layer: 1, zIndex: 0),
            Snapshot(layer: 1, zIndex: 0),
            Snapshot(layer: 2, zIndex: 7),
        ];
        var spriteIndices = new int[sprites.Length];
        SpritePlanning.Sort(sprites, spriteIndices);

        for (var position = 1; position < spriteIndices.Length; position++)
        {
            SpriteSnapshot previous = sprites[spriteIndices[position - 1]];
            SpriteSnapshot current = sprites[spriteIndices[position]];
            Assert.True(
                previous.Layer < current.Layer
                    || (previous.Layer == current.Layer && previous.ZIndex <= current.ZIndex),
                "Sprite indices must ascend by (Layer, ZIndex).");
        }

        Assert.Equal(
            Enumerable.Range(0, sprites.Length).Order(),
            spriteIndices.Order());

        CameraSnapshot[] cameras =
        [
            Camera(zIndex: 2),
            Camera(zIndex: 2),
            Camera(zIndex: 1),
            Camera(zIndex: 2),
        ];
        var cameraIndices = new int[cameras.Length];
        SpritePlanning.SortCameras(cameras, cameraIndices);

        for (var position = 1; position < cameraIndices.Length; position++)
        {
            Assert.True(
                cameras[cameraIndices[position - 1]].ZIndex
                    <= cameras[cameraIndices[position]].ZIndex,
                "Camera indices must ascend by ZIndex.");
        }

        Assert.Equal(
            Enumerable.Range(0, cameras.Length).Order(),
            cameraIndices.Order());
    }

    [Fact]
    public void LayerMatchUsesTheMaskBit()
    {
        Assert.True(SpritePlanning.LayerMatches(0b0101, 0b0001));
        Assert.True(SpritePlanning.LayerMatches(0b0101, 0b0100));
        Assert.False(SpritePlanning.LayerMatches(0b0101, 0b0010));
        Assert.False(SpritePlanning.LayerMatches(0, 1));
        Assert.True(SpritePlanning.LayerMatches(0x8000_0000, 0x8000_0000));
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    [InlineData(2u, true)]
    [InlineData(3u, false)]
    [InlineData(0x8000_0000u, true)]
    [InlineData(0xC000_0000u, false)]
    public void LayerValidityRequiresExactlyOneBit(uint layer, bool valid)
    {
        Assert.Equal(valid, SpritePlanning.IsValidLayer(layer));
    }

    [Fact]
    public void ClassifyReturnsTheApprovedSkipReasons()
    {
        Assert.Equal(
            SpriteSkipReason.Null,
            SpritePlanning.Classify(NullSnapshot(), texturePending: false));

        Assert.Equal(
            SpriteSkipReason.InvalidLayer,
            SpritePlanning.Classify(Snapshot(layer: 0), texturePending: false));
        Assert.Equal(
            SpriteSkipReason.InvalidLayer,
            SpritePlanning.Classify(Snapshot(layer: 3), texturePending: false));

        SpriteAsset indexed = Asset();
        indexed.Topology = SpriteTopology.TriangleStrip;
        indexed.Indices = [0, 1, 2, 3];
        Assert.Equal(
            SpriteSkipReason.NonQuad,
            SpritePlanning.Classify(
                Snapshot(asset: indexed, layer: 1),
                texturePending: false));

        Assert.Equal(
            SpriteSkipReason.Pending,
            SpritePlanning.Classify(Snapshot(layer: 1), texturePending: true));

        Assert.Equal(
            SpriteSkipReason.None,
            SpritePlanning.Classify(Snapshot(layer: 1), texturePending: false));
    }

    [Fact]
    public void UnknownTopologyIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Topology = (SpriteTopology)99;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpritePlanning.Classify(Snapshot(asset: asset, layer: 1), false));

        Assert.Contains("Topology", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityRuleUsesTheLayerMaskAndNeverCullsUnknownBounds()
    {
        SpriteSnapshot sprite = Snapshot(layer: 1);
        var cameraBounds = new Bounds(new Vector2(-10f, -10f), new Vector2(10f, 10f));

        Assert.True(SpritePlanning.IsVisible(0b1, cameraBounds, sprite));
        Assert.False(SpritePlanning.IsVisible(0b10, cameraBounds, sprite));
        Assert.False(SpritePlanning.IsVisible(0, cameraBounds, sprite));

        // Empty sprite bounds and empty camera bounds both mean "unknown" and never cull.
        Assert.True(SpritePlanning.IsVisible(0b1, Bounds.Empty, sprite));
        SpriteSnapshot outside = Bounded(
            0b1,
            new Bounds(new Vector2(100f, -1f), new Vector2(105f, 1f)));
        Assert.True(SpritePlanning.IsVisible(0b1, Bounds.Empty, outside));
        Assert.False(SpritePlanning.IsVisible(0b1, cameraBounds, outside));

        // Bounds that only touch the camera edge still intersect.
        SpriteSnapshot touching = Bounded(
            0b1,
            new Bounds(new Vector2(10f, -1f), new Vector2(15f, 1f)));
        Assert.True(SpritePlanning.IsVisible(0b1, cameraBounds, touching));

        SpriteSnapshot overlapping = Bounded(
            0b1,
            new Bounds(new Vector2(5f, 5f), new Vector2(15f, 15f)));
        Assert.True(SpritePlanning.IsVisible(0b1, cameraBounds, overlapping));
    }

    [Fact]
    public void QuadSizeUsesTheUvSpanAndPerAxisWorldScale()
    {
        SpriteAsset asset = Asset(
            uvs:
            [
                new Vector2(0.25f, 0.1f),
                new Vector2(0.75f, 0.1f),
                new Vector2(0.75f, 0.9f),
                new Vector2(0.25f, 0.9f),
            ],
            scale: new Vector2(2f, 5f),
            pivot: new Vector2(0.25f, 0.75f));

        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: false, flipY: false);

        Assert.Equal(1f, quad.WorldWidth, 5);
        Assert.Equal(4f, quad.WorldHeight, 5);
        Assert.Equal(1f, quad.SizeX, 5);
        Assert.Equal(4f, quad.SizeY, 5);
        Assert.Equal(new Vector2(0.25f, 0.75f), quad.Pivot);
    }

    [Fact]
    public void FlipsMirrorTheSignedSizeWithoutChangingThePivot()
    {
        SpriteAsset asset = Asset(scale: new Vector2(2f));
        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: true, flipY: false);

        Assert.Equal(-2f, quad.SizeX, 5);
        Assert.Equal(2f, quad.SizeY, 5);
        Assert.Equal(2f, quad.WorldWidth, 5);
        Assert.Equal(new Vector2(0.5f, 0.5f), quad.Pivot);
    }

    [Fact]
    public void UvsOutsideTheUnitRangeAreAllowed()
    {
        SpriteAsset asset = Asset(
            uvs:
            [
                new Vector2(-0.5f, -0.5f),
                new Vector2(1.5f, -0.5f),
                new Vector2(1.5f, 1.5f),
                new Vector2(-0.5f, 1.5f),
            ],
            scale: new Vector2(10f));

        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: false, flipY: false);

        Assert.Equal(20f, quad.WorldWidth, 5);
        Assert.Equal(20f, quad.WorldHeight, 5);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 1f)]
    [InlineData(1f, -1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.NaN)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 1f)]
    [InlineData(1f, float.NegativeInfinity)]
    public void InvalidScaleIsFatal(float x, float y)
    {
        SpriteAsset asset = Asset(scale: new Vector2(x, y));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Scale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullUvsIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Uvs = null;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Uvs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortUvsIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Uvs = [new Vector2(0f, 0f), new Vector2(1f, 1f)];

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Uvs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteUvIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Uvs =
        [
            new Vector2(0f, 0f),
            new Vector2(float.NaN, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        ];

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Uvs", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(float.NaN, 0.5f)]
    [InlineData(0.5f, float.PositiveInfinity)]
    [InlineData(-0.1f, 0.5f)]
    [InlineData(1.1f, 0.5f)]
    [InlineData(0.5f, -0.1f)]
    [InlineData(0.5f, 1.1f)]
    public void InvalidPivotIsFatal(float x, float y)
    {
        SpriteAsset asset = Asset(pivot: new Vector2(x, y));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Pivot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTopologyIsFatalInTheResolver()
    {
        SpriteAsset asset = Asset();
        asset.Topology = (SpriteTopology)99;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Topology", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndicesOnAQuadAreFatal()
    {
        SpriteAsset asset = Asset();
        asset.Indices = [0, 1, 2];

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), false, false));

        Assert.Contains("Indices", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    private static SpriteAsset Asset(
        Vector2[]? uvs = null,
        Vector2? scale = null,
        Vector2? pivot = null)
    {
        return new SpriteAsset
        {
            Texture = "textures/player",
            Uvs = uvs
                ??
                [
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                ],
            Scale = scale ?? new Vector2(16f),
            Pivot = pivot ?? new Vector2(0.5f, 0.5f),
            Topology = SpriteTopology.Quad,
            Indices = null,
        };
    }

    private static SpriteSnapshot Snapshot(
        SpriteAsset? asset = null,
        uint layer = 1,
        int zIndex = 0)
    {
        return new SpriteSnapshot(
            SpriteRenderData.Freeze(asset ?? Asset()),
            Matrix3x2.Identity,
            Color.White,
            layer,
            zIndex,
            false,
            false);
    }

    private static SpriteSnapshot NullSnapshot(uint layer = 1, int zIndex = 0)
    {
        return new SpriteSnapshot(
            null,
            Matrix3x2.Identity,
            Color.White,
            layer,
            zIndex,
            false,
            false);
    }

    private static SpriteSnapshot Bounded(uint layer, in Bounds bounds, int zIndex = 0)
        => Snapshot(layer: layer, zIndex: zIndex) with { Bounds = bounds };

    private static CameraSnapshot Camera(int zIndex)
        => new(
            ZIndex: zIndex,
            WorldMatrix: Matrix3x2.Identity,
            Size: 10f,
            Viewport: new Vector4(0f, 0f, 1f, 1f),
            CullingMask: 1,
            Clear: ClearKind.None,
            ClearColor: Color.Black);
}
