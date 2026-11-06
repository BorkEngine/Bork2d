using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render.Sprites;
using Bork2d.Render.TwoDimensional;
using Xunit;

namespace Bork2d.Tests.Render.TwoDimensional;

/// <summary>
/// Deterministic tests for 2D camera ordering, the per-record sort key, layer/bounds visibility,
/// sprite and line classification, and quad geometry/flips (M8 §6.4/§6.8/§8.1).
/// </summary>
public sealed class TwoDimensionalPlanningTests
{
    [Fact]
    public void SortKeyOrdersByZIndexThenScreenYThenScreenX()
    {
        TwoDimensionalSortKey[] keys =
        [
            new(zIndex: 2, screenY: 0f, screenX: 0f, DrawKind.Sprite, 0),
            new(zIndex: 1, screenY: 50f, screenX: 0f, DrawKind.Sprite, 1),
            new(zIndex: 1, screenY: 10f, screenX: 40f, DrawKind.Sprite, 2),
            new(zIndex: 1, screenY: 10f, screenX: 20f, DrawKind.Line, 3),
        ];

        Array.Sort(keys);

        Assert.Equal([3, 2, 1, 0], keys.Select(key => key.RecordIndex));
        Assert.Equal(DrawKind.Line, keys[0].Kind);
    }

    [Fact]
    public void SortKeyTiesIgnoreKindAndRecordIndex()
    {
        var first = new TwoDimensionalSortKey(3, 7f, 9f, DrawKind.Sprite, 1);
        var second = new TwoDimensionalSortKey(3, 7f, 9f, DrawKind.Line, 0);

        Assert.Equal(0, first.CompareTo(second));
        Assert.Equal(0, second.CompareTo(first));
    }

    [Fact]
    public void SortCamerasOrdersByZIndex()
    {
        CameraSnapshot[] cameras = [Camera(zIndex: 5), Camera(zIndex: 1), Camera(zIndex: 3)];
        var indices = new int[cameras.Length];

        TwoDimensionalPlanning.SortCameras(cameras, indices);

        Assert.Equal(new[] { 1, 2, 0 }, indices);
    }

    [Fact]
    public void SortCamerasRejectsAShortIndexBuffer()
    {
        CameraSnapshot[] cameras = [Camera(zIndex: 0), Camera(zIndex: 1)];

        Assert.Throws<ArgumentException>(
            () => TwoDimensionalPlanning.SortCameras(cameras, new int[1]));
    }

    [Fact]
    public void SortCamerasOrdersEqualKeysAsACompletePermutation()
    {
        CameraSnapshot[] cameras =
        [
            Camera(zIndex: 2),
            Camera(zIndex: 2),
            Camera(zIndex: 1),
            Camera(zIndex: 2),
        ];
        var cameraIndices = new int[cameras.Length];

        TwoDimensionalPlanning.SortCameras(cameras, cameraIndices);

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
        Assert.True(TwoDimensionalPlanning.LayerMatches(0b0101, 0b0001));
        Assert.True(TwoDimensionalPlanning.LayerMatches(0b0101, 0b0100));
        Assert.False(TwoDimensionalPlanning.LayerMatches(0b0101, 0b0010));
        Assert.False(TwoDimensionalPlanning.LayerMatches(0, 1));
        Assert.True(TwoDimensionalPlanning.LayerMatches(0x8000_0000, 0x8000_0000));
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
        Assert.Equal(valid, TwoDimensionalPlanning.IsValidLayer(layer));
    }

    [Fact]
    public void ClassifyReturnsTheApprovedSpriteSkipReasons()
    {
        Assert.Equal(
            SpriteSkipReason.Null,
            TwoDimensionalPlanning.Classify(NullSnapshot(), texturePending: false));

        Assert.Equal(
            SpriteSkipReason.InvalidLayer,
            TwoDimensionalPlanning.Classify(Snapshot(layer: 0), texturePending: false));
        Assert.Equal(
            SpriteSkipReason.InvalidLayer,
            TwoDimensionalPlanning.Classify(Snapshot(layer: 3), texturePending: false));

        SpriteAsset indexed = Asset();
        indexed.Topology = SpriteTopology.TriangleStrip;
        indexed.Indices = [0, 1, 2, 3];
        Assert.Equal(
            SpriteSkipReason.NonQuad,
            TwoDimensionalPlanning.Classify(
                Snapshot(asset: indexed, layer: 1),
                texturePending: false));

        Assert.Equal(
            SpriteSkipReason.Pending,
            TwoDimensionalPlanning.Classify(Snapshot(layer: 1), texturePending: true));

        Assert.Equal(
            SpriteSkipReason.None,
            TwoDimensionalPlanning.Classify(Snapshot(layer: 1), texturePending: false));
    }

    [Fact]
    public void UnknownSpriteTopologyIsFatal()
    {
        SpriteAsset asset = Asset();
        asset.Topology = (SpriteTopology)99;

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => TwoDimensionalPlanning.Classify(Snapshot(asset: asset, layer: 1), false));

        Assert.Contains("Topology", exception.Message, StringComparison.Ordinal);
        Assert.Contains("textures/player", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifyLineSkipsNullGeometryInvalidLayerAndPendingShaders()
    {
        LineSnapshot drawable = LineForTest(geometry: BuildGeometry(), layer: 1u);

        Assert.Equal(
            LineSkipReason.NullGeometry,
            TwoDimensionalPlanning.ClassifyLine(
                LineForTest(geometry: null, layer: 1u),
                shaderPending: false));
        Assert.Equal(
            LineSkipReason.InvalidLayer,
            TwoDimensionalPlanning.ClassifyLine(
                drawable with { Layer = 0 },
                shaderPending: false));
        Assert.Equal(
            LineSkipReason.InvalidLayer,
            TwoDimensionalPlanning.ClassifyLine(
                drawable with { Layer = 3 },
                shaderPending: false));
        Assert.Equal(
            LineSkipReason.Pending,
            TwoDimensionalPlanning.ClassifyLine(drawable, shaderPending: true));
        Assert.Equal(
            LineSkipReason.None,
            TwoDimensionalPlanning.ClassifyLine(drawable, shaderPending: false));
    }

    [Fact]
    public void NullGeometryIsCheckedBeforeTheLayer()
    {
        Assert.Equal(
            LineSkipReason.NullGeometry,
            TwoDimensionalPlanning.ClassifyLine(
                LineForTest(geometry: null, layer: 0u),
                shaderPending: true));
    }

    [Fact]
    public void VisibilityRuleUsesTheLayerMaskAndNeverCullsUnknownBounds()
    {
        var cameraBounds = Bounds.FromMinMax(new Vector2(-10f, -10f), new Vector2(10f, 10f));
        Bounds recordBounds = Bounds.FromMinMax(new Vector2(-1f, -1f), new Vector2(1f, 1f));

        Assert.True(TwoDimensionalPlanning.IsVisible(0b1, cameraBounds, 0b1, recordBounds));
        Assert.False(TwoDimensionalPlanning.IsVisible(0b10, cameraBounds, 0b1, recordBounds));
        Assert.False(TwoDimensionalPlanning.IsVisible(0, cameraBounds, 0b1, recordBounds));

        // Empty record bounds and empty camera bounds both mean "unknown" and never cull.
        Assert.True(TwoDimensionalPlanning.IsVisible(0b1, Bounds.Empty, 0b1, recordBounds));
        Assert.True(TwoDimensionalPlanning.IsVisible(0b1, cameraBounds, 0b1, Bounds.Empty));

        Bounds outside = Bounds.FromMinMax(new Vector2(100f, -1f), new Vector2(105f, 1f));
        Assert.True(TwoDimensionalPlanning.IsVisible(0b1, Bounds.Empty, 0b1, outside));
        Assert.False(TwoDimensionalPlanning.IsVisible(0b1, cameraBounds, 0b1, outside));

        // Bounds that only touch the camera edge still intersect.
        Bounds touching = Bounds.FromMinMax(new Vector2(10f, -1f), new Vector2(15f, 1f));
        Assert.True(TwoDimensionalPlanning.IsVisible(0b1, cameraBounds, 0b1, touching));
    }

    [Fact]
    public void BoundsIntersectKeepsEachSideConservative()
    {
        Bounds cameraBounds = Bounds.FromMinMax(new Vector2(-10f, -10f), new Vector2(10f, 10f));

        Assert.True(TwoDimensionalPlanning.BoundsIntersect(cameraBounds, Bounds.Empty));
        Assert.True(TwoDimensionalPlanning.BoundsIntersect(Bounds.Empty, Bounds.Empty));
        Assert.False(
            TwoDimensionalPlanning.BoundsIntersect(
                cameraBounds,
                Bounds.FromMinMax(new Vector2(20f, 20f), new Vector2(30f, 30f))));
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

    [Fact]
    public void ClassifyFontSkipsNullInvalidAndPendingButNotEmpty()
    {
        FontAsset asset = Bork2d.Tests.TestSupport.FontTestSupport.CreateFont();
        FontRenderData data = FontRenderData.Freeze(asset);
        FontLayoutData empty = FontLayoutData.Empty;
        FontLayoutData layout = FontLayoutBuilder.Build(
            data.Glyphs,
            data.Kerning,
            data.Ascent,
            data.Descent,
            data.LineGap,
            "A",
            size: 2f,
            TextAlignment.Left,
            lineSpacing: 1f,
            missingGlyph: null);

        FontSnapshot nullFont = new(null, layout, Matrix3x2.Identity, Color.White, 1u, 0);
        FontSnapshot invalidLayer = new(data, layout, Matrix3x2.Identity, Color.White, 0u, 0);
        FontSnapshot emptyLayout = new(data, empty, Matrix3x2.Identity, Color.White, 1u, 0);
        FontSnapshot drawable = new(data, layout, Matrix3x2.Identity, Color.White, 1u, 0);

        Assert.Equal(
            FontSkipReason.Null,
            TwoDimensionalPlanning.ClassifyFont(nullFont, shaderPending: true, atlasPending: true));
        Assert.Equal(
            FontSkipReason.InvalidLayer,
            TwoDimensionalPlanning.ClassifyFont(
                invalidLayer,
                shaderPending: false,
                atlasPending: false));
        Assert.Equal(
            FontSkipReason.Empty,
            TwoDimensionalPlanning.ClassifyFont(
                emptyLayout,
                shaderPending: true,
                atlasPending: true));
        Assert.Equal(
            FontSkipReason.PendingShader,
            TwoDimensionalPlanning.ClassifyFont(
                drawable,
                shaderPending: true,
                atlasPending: false));
        Assert.Equal(
            FontSkipReason.PendingAtlas,
            TwoDimensionalPlanning.ClassifyFont(
                drawable,
                shaderPending: false,
                atlasPending: true));
        Assert.Equal(
            FontSkipReason.None,
            TwoDimensionalPlanning.ClassifyFont(
                drawable,
                shaderPending: false,
                atlasPending: false));
    }

    [Fact]
    public void SortKeyOrdersFontGlyphsByZIndexThenScreenYThenScreenX()
    {
        TwoDimensionalSortKey[] keys =
        [
            new(zIndex: 1, screenY: 10f, screenX: 30f, DrawKind.Font, 2),
            new(zIndex: 1, screenY: 10f, screenX: 10f, DrawKind.Font, 0),
            new(zIndex: 1, screenY: 10f, screenX: 20f, DrawKind.Font, 1),
            new(zIndex: 0, screenY: 50f, screenX: 0f, DrawKind.Font, 3),
        ];

        Array.Sort(keys);

        Assert.Equal([3, 0, 1, 2], keys.Select(key => key.RecordIndex));
    }

    private static LineGeometryData BuildGeometry()
    {
        LineGeometryData? geometry = LineGeometryBuilder.Build(
            [new Vector2(0f, 0f), new Vector2(10f, 0f)],
            closed: false,
            width: 2f);
        return Assert.IsType<LineGeometryData>(geometry);
    }

    private static LineSnapshot LineForTest(LineGeometryData? geometry, uint layer)
        => new(
            geometry,
            Matrix3x2.Identity,
            Color.White,
            layer,
            ZIndex: 0,
            AntiAlias: true);

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
