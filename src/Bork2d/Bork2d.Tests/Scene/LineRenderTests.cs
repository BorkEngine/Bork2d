using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Deterministic tests for the line renderable contract: defaults, point copy semantics, and the
/// strict instance-uniform rules shared with sprites.
/// </summary>
public sealed class LineRenderTests
{
    [Fact]
    public void LineRenderHasTheDeclaredDefaults()
    {
        var engine = TestEngineFactory.Create();

        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);

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
    public void LineValuesRoundTrip()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);

        line.Points = [new Vector2(1f, 2f), new Vector2(3f, 4f)];
        line.Closed = true;
        line.Width = 2.5f;
        line.Color = Color.Red;
        line.AntiAlias = false;
        line.Layer = GameLayer.Player;
        line.ZIndex = -7;

        Assert.Equal([new Vector2(1f, 2f), new Vector2(3f, 4f)], line.Points);
        Assert.True(line.Closed);
        Assert.Equal(2.5f, line.Width);
        Assert.Equal(Color.Red, line.Color);
        Assert.False(line.AntiAlias);
        Assert.Equal(GameLayer.Player, line.Layer);
        Assert.Equal(-7, line.ZIndex);
    }

    [Fact]
    public void CreateLineRenderRequiresAnEngineParent()
    {
        var engine = TestEngineFactory.Create();

        Assert.Throws<ArgumentNullException>(() => { engine.CreateLineRender(null!); });
        Assert.Throws<Bork2dException>(() => { engine.CreateLineRender(new FakeTransform()); });
    }

    [Fact]
    public void PointsSetterCopiesValuesAndTheGetterIsAStableReadOnlyView()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        Vector2[] points = [new Vector2(0f, 0f), new Vector2(10f, 0f)];

        line.Points = points;
        points[1] = new Vector2(99f, 99f);

        Assert.Equal(new Vector2(10f, 0f), line.Points[1]);
        Assert.Same(line.Points, line.Points);
        Assert.False(line.Points is Vector2[]);
        Assert.Throws<NotSupportedException>(
            () => ((IList<Vector2>)line.Points)[0] = Vector2.Zero);
    }

    [Fact]
    public void AssigningNullPointsStoresEmptyAndClearsTheGeometry()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Points = [new Vector2(0f, 0f), new Vector2(10f, 0f)];
        Assert.False(line.Bounds.IsEmpty);

        line.Points = null!;

        Assert.Empty(line.Points);
        Assert.Equal(Bounds.Empty, line.Bounds);
        Assert.Null(Capture(engine).Lines[0].Geometry);
    }

    [Fact]
    public void SetUniformWithoutAShaderThrows()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => line.SetUniform("value", 1f));

        Assert.Contains("value", error.Message, StringComparison.Ordinal);
        Assert.Contains("Shader", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetUniformWithAnUnknownNameThrows()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = AllTypesShader();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => line.SetUniform("missing", 1f));

        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetUniformWithTheWrongTypeThrowsAndNamesTheExpectedType()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = AllTypesShader();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => line.SetUniform("v2", 1f));

        Assert.Contains("v2", error.Message, StringComparison.Ordinal);
        Assert.Contains(ShaderUniformType.Vector2.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetUniformWithAnEmptyNameThrows(string? name)
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = AllTypesShader();

        Assert.ThrowsAny<ArgumentException>(() => line.SetUniform(name!, 1f));
    }

    [Fact]
    public void ResetUniformsRestoresTheShaderDefaults()
    {
        var engine = TestEngineFactory.Create();
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = AllTypesShader();
        line.SetUniform("f", 10f);
        Assert.Equal(10f, Capture(engine).Lines[0].Uniform0.X);

        line.ResetUniforms();

        Assert.Equal(0.5f, Capture(engine).Lines[0].Uniform0.X);
    }

    [Fact]
    public void ReassigningTheShaderKeepsStoredValuesAndOnlyMatchingOnesApply()
    {
        var engine = TestEngineFactory.Create();
        var floatShader = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("value", ShaderUniformType.Float, 0, Vector4.Zero),
            ],
        };
        var vectorShader = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("value", ShaderUniformType.Vector2, 0, Vector4.Zero),
            ],
        };
        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Shader = floatShader;
        line.SetUniform("value", 5f);

        line.Shader = vectorShader;
        Assert.Equal(Vector4.Zero, Capture(engine).Lines[0].Uniform0);

        line.Shader = floatShader;
        Assert.Equal(new Vector4(5f, 0f, 0f, 0f), Capture(engine).Lines[0].Uniform0);
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

    private static FrameSnapshot Capture(GameEngine<GameLayer> engine)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        return snapshot;
    }
}
