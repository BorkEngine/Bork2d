using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for line capture: snapshot values, world matrices, geometry reuse, shader
/// freeze and uniform packing, invalid geometry, layers, and bounds.
/// </summary>
public sealed class LineSnapshotTests
{
    [Fact]
    public void LineIsCapturedWithItsStateAndGeometry()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(0f, 0f), new Vector2(10f, 0f)];
        line.Width = 2f;
        line.Color = Color.Yellow;
        line.AntiAlias = false;
        line.Layer = GameLayer.Player;
        line.ZIndex = -4;

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(1, snapshot.LineCount);
        LineSnapshot captured = snapshot.Lines[0];
        Assert.NotNull(captured.Geometry);
        Assert.Equal(4, captured.Geometry!.VertexCount);
        Assert.Equal(Color.Yellow, captured.Color);
        Assert.Equal((uint)GameLayer.Player, captured.Layer);
        Assert.Equal(-4, captured.ZIndex);
        Assert.False(captured.AntiAlias);
        Assert.Equal(2f, captured.Width);
        Assert.Equal(Matrix3x2.Identity, captured.WorldMatrix);
        Assert.False(captured.Bounds.IsEmpty);
    }

    [Fact]
    public void CaptureWithoutPointsCarriesSafeDefaultsAndEmptyBounds()
    {
        var engine = TestEngineFactory.Create();
        engine.CreateLineRender(engine.Root);

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(1, snapshot.LineCount);
        LineSnapshot captured = snapshot.Lines[0];
        Assert.Null(captured.Geometry);
        Assert.Equal(Bounds.Empty, captured.Bounds);
        Assert.Null(captured.ShaderData);
        Assert.Equal(Vector4.Zero, captured.Uniform0);
        Assert.Equal(Vector4.Zero, captured.Uniform1);
        Assert.Equal(Vector4.Zero, captured.Uniform2);
        Assert.Equal(Vector4.Zero, captured.Uniform3);
        Assert.Equal(1f, captured.Width);
        Assert.Equal(Color.White, captured.Color);
        Assert.True(captured.AntiAlias);
        Assert.Equal(0u, captured.Layer);
        Assert.Equal(0, captured.ZIndex);
    }

    [Fact]
    public void NestedTransformsProduceTheComposedWorldMatrix()
    {
        var engine = TestEngineFactory.Create();
        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(10f, 20f);
        parent.LocalScale = new Vector2(2f, 2f);
        parent.LocalRotation = 90f;
        ILineRender<GameLayer> line = engine.CreateLineRender(parent);
        line.Points = [new Vector2(0f, 0f), new Vector2(1f, 0f)];
        line.LocalPosition = new Vector2(1f, 0f);

        LineSnapshot captured = Capture(engine).Lines[0];

        var expected = Matrix3x2.CreateTranslation(1f, 0f)
            * Matrix3x2.CreateScale(2f, 2f)
            * Matrix3x2.CreateRotation(MathF.PI / 2f)
            * Matrix3x2.CreateTranslation(10f, 20f);
        TestAssertions.Close(expected, captured.WorldMatrix);
        TestAssertions.Close(new Vector2(10f, 22f), captured.WorldMatrix.Translation);
    }

    [Fact]
    public void GeometryIsReusedUntilPointsWidthOrClosedChange()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        Vector2[] square =
        [
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f),
            new Vector2(0f, 10f),
        ];
        line.Points = square;
        LineGeometryData first = Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.Same(first, Capture(engine).Lines[0].Geometry);

        line.Width = 4f;
        LineGeometryData wider = Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.NotSame(first, wider);

        // Reassigning the same value is not a change and does not rebuild.
        line.Width = 4f;
        Assert.Same(wider, Capture(engine).Lines[0].Geometry);

        line.Closed = true;
        LineGeometryData closed =
            Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.NotSame(wider, closed);

        line.Closed = false;
        LineGeometryData reopened =
            Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.NotSame(closed, reopened);

        line.Points = square;
        LineGeometryData recopied =
            Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.NotSame(reopened, recopied);
    }

    [Fact]
    public void ColorAntiAliasShaderLayerZIndexAndTransformDoNotRebuildGeometry()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f)];
        LineGeometryData first = Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);

        line.Color = Color.Red;
        line.AntiAlias = false;
        line.Shader = AllTypesShader();
        line.Layer = GameLayer.Player;
        line.ZIndex = 7;
        line.LocalPosition = new Vector2(5f, 6f);
        line.LocalRotation = 30f;
        line.LocalScale = new Vector2(2f, 3f);

        LineGeometryData after = Assert.IsType<LineGeometryData>(Capture(engine).Lines[0].Geometry);
        Assert.Same(first, after);
    }

    [Fact]
    public void CaptureCarriesTheFrozenShaderMetadataAndDefaultBlock()
    {
        var engine = TestEngineFactory.Create();
        ShaderAsset asset = AllTypesShader();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(0f, 0f), new Vector2(1f, 0f)];
        line.Shader = asset;

        LineSnapshot captured = Capture(engine).Lines[0];

        Assert.Same(ShaderRenderData.Freeze(asset), captured.ShaderData);
        Assert.Equal(new Vector4(0.5f, 1f, 2f, 3f), captured.Uniform0);
        Assert.Equal(new Vector4(4f, 5f, 6f, 7f), captured.Uniform1);
        Assert.Equal(new Vector4(8f, 9f, 1f, 1f), captured.Uniform2);
        Assert.Equal(new Vector4(1f, 1f, 0f, 0f), captured.Uniform3);
    }

    [Fact]
    public void MutatingTheShaderAssetAfterAssignmentDoesNotChangeTheCapture()
    {
        var engine = TestEngineFactory.Create();
        ShaderAsset asset = AllTypesShader();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = asset;

        asset.EntryPoint = "other";
        asset.LayoutVersion = 99;
        asset.InstanceUniforms[0] = new ShaderUniform("f", ShaderUniformType.Float, 0, Vector4.Zero);
        asset.InstanceUniforms = [new ShaderUniform("f", ShaderUniformType.Float, 0, Vector4.One)];

        LineSnapshot captured = Capture(engine).Lines[0];

        Assert.Same(ShaderRenderData.Freeze(asset), captured.ShaderData);
        Assert.Equal("main", captured.ShaderData!.EntryPoint);
        Assert.Equal(0u, captured.ShaderData.LayoutVersion);
        Assert.Equal(new Vector4(0.5f, 1f, 2f, 3f), captured.Uniform0);
    }

    [Fact]
    public void SetUniformStoresEveryOverloadAndCaptureAppliesThem()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = AllTypesShader();

        line.SetUniform("f", 10f);
        line.SetUniform("v2", new Vector2(20f, 21f));
        line.SetUniform("v3", new Vector3(30f, 31f, 32f));
        line.SetUniform("v4", new Vector4(40f, 41f, 42f, 43f));
        line.SetUniform("c", new Color(0.1f, 0.2f, 0.3f, 0.4f));

        LineSnapshot captured = Capture(engine).Lines[0];

        Assert.Equal(new Vector4(10f, 20f, 21f, 30f), captured.Uniform0);
        Assert.Equal(new Vector4(31f, 32f, 40f, 41f), captured.Uniform1);
        Assert.Equal(new Vector4(42f, 43f, 0.1f, 0.2f), captured.Uniform2);
        Assert.Equal(new Vector4(0.3f, 0.4f, 0f, 0f), captured.Uniform3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidGeometryIsCapturedAsNotDrawableWithoutThrowing(bool useNonFinitePoint)
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = useNonFinitePoint
            ? [new Vector2(0f, 0f), new Vector2(float.NaN, 1f)]
            : [new Vector2(0f, 0f), new Vector2(1f, 0f)];
        if (!useNonFinitePoint)
        {
            line.Width = 0f;
        }

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(1, snapshot.LineCount);
        Assert.Null(snapshot.Lines[0].Geometry);
        Assert.Equal(Bounds.Empty, snapshot.Lines[0].Bounds);
        Assert.Equal(Bounds.Empty, line.Bounds);
    }

    [Fact]
    public void InvalidLayersAreCapturedAsIs()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> zero = engine.CreateLineRender(engine.Root);
        zero.Layer = GameLayer.None;
        ILineRender<GameLayer> multiBit = engine.CreateLineRender(engine.Root);
        multiBit.Layer = (GameLayer)((int)GameLayer.World | (int)GameLayer.Player);

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(2, snapshot.LineCount);
        var layers = new uint[snapshot.LineCount];
        for (var index = 0; index < layers.Length; index++)
        {
            layers[index] = snapshot.Lines[index].Layer;
        }

        Array.Sort(layers);
        Assert.Equal(new[] { 0u, 3u }, layers);
    }

    [Fact]
    public void Bit31SurvivesTheLayerFlattening()
    {
        var engine = new GameEngine<WideLayers>(new FakeTime(), new FakeWindow(), new FakeAssets());
        ILineRender<WideLayers> line = engine.CreateLineRender(engine.Root);
        line.Layer = WideLayers.High;

        FrameSnapshot snapshot = Capture(engine);

        Assert.Equal(0x8000_0000u, snapshot.Lines[0].Layer);
    }

    [Fact]
    public void BoundsFollowRotationScaleAndTranslation()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(-1f, 0f), new Vector2(1f, 0f)];
        line.Width = 1f;

        AssertBounds(line.Bounds, new Vector2(-1f, -0.5f), new Vector2(1f, 0.5f));

        line.LocalScale = new Vector2(2f, 3f);
        AssertBounds(line.Bounds, new Vector2(-2f, -1.5f), new Vector2(2f, 1.5f));

        line.LocalScale = Vector2.One;
        line.LocalRotation = 90f;
        line.LocalPosition = new Vector2(3f, 4f);
        AssertBounds(line.Bounds, new Vector2(2.5f, 3f), new Vector2(3.5f, 5f));
    }

    [Fact]
    public void BoundsAreEmptyForMissingGeometryInvalidTransformsAndNonFiniteValues()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        Assert.Equal(Bounds.Empty, line.Bounds);

        line.Points = [new Vector2(0f, 0f), new Vector2(10f, 0f)];
        line.Width = 0f;
        Assert.Equal(Bounds.Empty, line.Bounds);

        line.Width = 1f;
        line.LocalScale = Vector2.Zero;
        Assert.Equal(Bounds.Empty, line.Bounds);

        line.LocalScale = new Vector2(float.NaN, 1f);
        Assert.Equal(Bounds.Empty, line.Bounds);

        line.LocalScale = Vector2.One;
        line.LocalPosition = new Vector2(float.PositiveInfinity, 0f);
        Assert.Equal(Bounds.Empty, line.Bounds);
    }

    [Fact]
    public void CapturedBoundsMatchTheLiveBounds()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(-2f, 1f), new Vector2(3f, 1f), new Vector2(3f, 6f)];
        line.Width = 0.5f;
        line.LocalRotation = 25f;
        line.LocalPosition = new Vector2(4f, -3f);

        LineSnapshot captured = Capture(engine).Lines[0];

        Assert.False(captured.Bounds.IsEmpty);
        TestAssertions.Close(line.Bounds.Min, captured.Bounds.Min);
        TestAssertions.Close(line.Bounds.Max, captured.Bounds.Max);
    }

    private static ShaderAsset AllTypesShader() => new()
    {
        InstanceUniforms =
        [
            new ShaderUniform("f", ShaderUniformType.Float, 0, new Vector4(0.5f, 0f, 0f, 0f)),
            new ShaderUniform("v2", ShaderUniformType.Vector2, 1, new Vector4(1f, 2f, 0f, 0f)),
            new ShaderUniform("v3", ShaderUniformType.Vector3, 3, new Vector4(3f, 4f, 5f, 0f)),
            new ShaderUniform("v4", ShaderUniformType.Vector4, 6, new Vector4(6f, 7f, 8f, 9f)),
            new ShaderUniform("c", ShaderUniformType.Color, 10, new Vector4(1f, 1f, 1f, 1f)),
        ],
    };

    private static FrameSnapshot Capture<TLayer>(GameEngine<TLayer> engine)
        where TLayer : struct, Enum
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        return snapshot;
    }

    private static void AssertBounds(Bounds bounds, Vector2 min, Vector2 max)
    {
        Assert.False(bounds.IsEmpty);
        TestAssertions.Close(min, bounds.Min);
        TestAssertions.Close(max, bounds.Max);
    }

    [Flags]
    private enum WideLayers
    {
        None = 0,
        Low = 1,
        High = int.MinValue,
    }
}
