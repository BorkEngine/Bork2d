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
    public void FilterUsesTheCameraMaskAndKeepsSortOrder()
    {
        SpriteSnapshot[] sprites =
        [
            NullSnapshot(layer: 1, zIndex: 0),
            Snapshot(layer: 1, zIndex: 2),
            Snapshot(layer: 2, zIndex: 1),
            Snapshot(layer: 4, zIndex: 3),
            Snapshot(layer: 0, zIndex: 4),
            Snapshot(layer: 3, zIndex: 5),
        ];
        var sorted = new int[sprites.Length];
        var visible = new int[sprites.Length];
        SpritePlanning.Sort(sprites, sorted);

        int count = SpritePlanning.FilterVisible(sprites, sorted, 0b0011, visible);

        Assert.Equal(2, count);
        Assert.Equal(1, visible[0]);
        Assert.Equal(2, visible[1]);
    }

    [Fact]
    public void ZeroMaskSeesNothing()
    {
        SpriteSnapshot[] sprites = [Snapshot(layer: 1), Snapshot(layer: 2)];
        var sorted = new int[sprites.Length];
        var visible = new int[sprites.Length];
        SpritePlanning.Sort(sprites, sorted);

        Assert.Equal(0, SpritePlanning.FilterVisible(sprites, sorted, 0, visible));
    }

    [Fact]
    public void FilterRejectsAShortVisibleBuffer()
    {
        SpriteSnapshot[] sprites = [Snapshot(layer: 1), Snapshot(layer: 1)];
        var sorted = new int[sprites.Length];
        SpritePlanning.Sort(sprites, sorted);

        Assert.Throws<ArgumentException>(
            () => SpritePlanning.FilterVisible(sprites, sorted, 1, new int[1]));
    }

    [Fact]
    public void QuadSizeUsesTheUvSpanAndPixelsPerUnitScale()
    {
        SpriteAsset asset = Asset(
            uvs:
            [
                new Vector2(0.25f, 0.1f),
                new Vector2(0.75f, 0.1f),
                new Vector2(0.75f, 0.9f),
                new Vector2(0.25f, 0.9f),
            ],
            scale: 10f,
            pivot: new Vector2(0.25f, 0.75f));

        SpriteQuad quad = SpriteQuad.Resolve(asset, 4, 8, flipX: false, flipY: false);

        Assert.Equal(0.2f, quad.WorldWidth, 5);
        Assert.Equal(0.64f, quad.WorldHeight, 5);
        Assert.Equal(0.2f, quad.SizeX, 5);
        Assert.Equal(0.64f, quad.SizeY, 5);
        Assert.Equal(new Vector2(0.25f, 0.75f), quad.Pivot);
    }

    [Fact]
    public void FlipsMirrorTheSignedSizeWithoutChangingThePivot()
    {
        SpriteAsset asset = Asset(scale: 8f);
        SpriteQuad quad = SpriteQuad.Resolve(asset, 16, 16, flipX: true, flipY: false);

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
            scale: 10f);

        SpriteQuad quad = SpriteQuad.Resolve(asset, 4, 4, flipX: false, flipY: false);

        Assert.Equal(0.8f, quad.WorldWidth, 5);
        Assert.Equal(0.8f, quad.WorldHeight, 5);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidScaleIsFatal(float scale)
    {
        SpriteAsset asset = Asset(scale: scale);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

        Assert.Contains("Scale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullUvsIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Uvs = null;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

        Assert.Contains("Uvs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortUvsIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Uvs = [new Vector2(0f, 0f), new Vector2(1f, 1f)];

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

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
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

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
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

        Assert.Contains("Pivot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTopologyIsFatalInTheResolver()
    {
        SpriteAsset asset = Asset();
        asset.Topology = (SpriteTopology)99;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

        Assert.Contains("Topology", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IndicesOnAQuadAreFatal()
    {
        SpriteAsset asset = Asset();
        asset.Indices = [0, 1, 2];

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => SpriteQuad.Resolve(asset, 1, 1, false, false));

        Assert.Contains("Indices", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    private static SpriteAsset Asset(
        Vector2[]? uvs = null,
        float scale = 16f,
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
            Scale = scale,
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
            asset ?? Asset(),
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
}
