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
/// Deterministic tests for font capture: snapshot values, world matrices, bounds, the null-font
/// path, and the shared custom-shader and instance-uniform lanes.
/// </summary>
public sealed class FontSnapshotTests
{
    [Fact]
    public void FontIsCapturedWithItsStateLayoutAndWorldMatrix()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        FontAsset asset = FontTestSupport.CreateFont();
        ShaderAsset shader = FontTestSupport.CreateFloatShader();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = asset;
        font.Text = "AB";
        font.Size = 2f;
        font.Alignment = TextAlignment.Right;
        font.LineSpacing = 1.5f;
        font.Color = Color.Yellow;
        font.Layer = GameLayer.Player;
        font.ZIndex = -4;
        font.Shader = shader;
        font.SetUniform("uAlpha", 0.25f);
        font.LocalPosition = new Vector2(1f, 2f);
        font.LocalScale = new Vector2(2f, 3f);

        FrameSnapshot snapshot = FontTestSupport.Capture(engine);

        Assert.Equal(1, snapshot.FontCount);
        FontSnapshot captured = snapshot.Fonts[0];
        Assert.Same(FontRenderData.Freeze(asset), captured.FontData);
        FontLayoutData layout = Assert.IsType<FontLayoutData>(captured.Layout);
        TestAssertions.Close(2.5f, layout.Width);
        TestAssertions.Close(FontTestSupport.LineHeight * 3f, layout.Height);
        TestAssertions.Close(
            Matrix3x2.CreateScale(2f, 3f) * Matrix3x2.CreateTranslation(1f, 2f),
            captured.WorldMatrix);
        Assert.Equal(Color.Yellow, captured.Color);
        Assert.Equal((uint)GameLayer.Player, captured.Layer);
        Assert.Equal(-4, captured.ZIndex);
        Assert.False(captured.Bounds.IsEmpty);
        Assert.Same(ShaderRenderData.Freeze(shader), captured.ShaderData);
        Assert.Equal(new Vector4(0.25f, 0f, 0f, 0f), captured.Uniform0);
        Assert.Equal(Vector4.Zero, captured.Uniform1);
        Assert.Equal(Vector4.Zero, captured.Uniform2);
        Assert.Equal(Vector4.Zero, captured.Uniform3);
    }

    [Fact]
    public void NullFontIsCapturedSafely()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);

        FrameSnapshot snapshot = FontTestSupport.Capture(engine);

        Assert.Equal(1, snapshot.FontCount);
        FontSnapshot captured = snapshot.Fonts[0];
        Assert.Null(captured.FontData);
        Assert.Same(FontLayoutData.Empty, captured.Layout);
        Assert.Equal(Bounds.Empty, captured.Bounds);
        Assert.Null(captured.ShaderData);
        Assert.Equal(Vector4.Zero, captured.Uniform0);
        Assert.Equal(Bounds.Empty, font.Bounds);
    }

    [Fact]
    public void BoundsCoverTheLaidOutQuadsUnderTheComposedWorldTransform()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A";

        AssertBounds(font.Bounds, new Vector2(0.05f, 0f), new Vector2(0.55f, 0.6f));

        var parent = engine.CreateTransform(engine.Root);
        parent.LocalPosition = new Vector2(10f, 20f);
        font.Parent = parent;
        font.LocalPosition = Vector2.Zero;
        AssertBounds(font.Bounds, new Vector2(10.05f, 20f), new Vector2(10.55f, 20.6f));

        parent.LocalPosition = Vector2.Zero;
        parent.LocalRotation = 90f;
        AssertBounds(font.Bounds, new Vector2(-0.6f, 0.05f), new Vector2(0f, 0.55f));

        FontSnapshot captured = FontTestSupport.Capture(engine).Fonts[0];
        TestAssertions.Close(font.Bounds.Min, captured.Bounds.Min);
        TestAssertions.Close(font.Bounds.Max, captured.Bounds.Max);
    }

    [Fact]
    public void BoundsAreEmptyWithoutDrawableQuads()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        Assert.Equal(Bounds.Empty, font.Bounds);

        FontAsset asset = FontTestSupport.CreateFont();
        font.Font = asset;
        font.Text = string.Empty;
        Assert.Equal(Bounds.Empty, font.Bounds);

        font.Text = " ";
        FontLayoutData spaceLayout = Layout(engine);
        Assert.True(spaceLayout.IsEmpty);
        TestAssertions.Close(0.3f, spaceLayout.Width);
        Assert.Equal(Bounds.Empty, font.Bounds);
        Assert.Equal(Bounds.Empty, FontTestSupport.Capture(engine).Fonts[0].Bounds);

        font.Text = "A";
        font.Size = 0f;
        Assert.Equal(Bounds.Empty, font.Bounds);
        Assert.Equal(Bounds.Empty, FontTestSupport.Capture(engine).Fonts[0].Bounds);
    }

    [Fact]
    public void BoundsAreEmptyForNonFiniteOrDegenerateTransforms()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A";

        font.LocalScale = Vector2.Zero;
        Assert.Equal(Bounds.Empty, font.Bounds);

        font.LocalScale = new Vector2(float.NaN, 1f);
        Assert.Equal(Bounds.Empty, font.Bounds);

        font.LocalScale = Vector2.One;
        font.LocalPosition = new Vector2(float.PositiveInfinity, 0f);
        Assert.Equal(Bounds.Empty, font.Bounds);
        Assert.Equal(Bounds.Empty, FontTestSupport.Capture(engine).Fonts[0].Bounds);
    }

    [Fact]
    public void CaptureWithoutAShaderLeavesSafeDefaults()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A";

        FontSnapshot captured = FontTestSupport.Capture(engine).Fonts[0];

        Assert.Null(captured.ShaderData);
        Assert.Equal(Vector4.Zero, captured.Uniform0);
        Assert.Equal(Vector4.Zero, captured.Uniform1);
        Assert.Equal(Vector4.Zero, captured.Uniform2);
        Assert.Equal(Vector4.Zero, captured.Uniform3);
    }

    [Fact]
    public void ResetUniformsRestoresTheShaderDefaults()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A";
        font.Shader = FontTestSupport.CreateFloatShader();
        font.SetUniform("uAlpha", 0.25f);
        Assert.Equal(0.25f, FontTestSupport.Capture(engine).Fonts[0].Uniform0.X);

        font.ResetUniforms();

        Assert.Equal(0.75f, FontTestSupport.Capture(engine).Fonts[0].Uniform0.X);
    }

    [Fact]
    public void SetUniformValidatesAgainstTheFontShader()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();

        ArgumentException noShader = Assert.Throws<ArgumentException>(
            () => font.SetUniform("uAlpha", 1f));
        Assert.Contains("Shader", noShader.Message, StringComparison.Ordinal);

        font.Shader = FontTestSupport.CreateFloatShader();

        Assert.Throws<ArgumentException>(() => font.SetUniform("missing", 1f));
        Assert.Throws<ArgumentException>(() => font.SetUniform("uAlpha", new Vector2(1f, 2f)));
    }

    [Fact]
    public void ReassigningTheShaderKeepsStoredValuesAndOnlyMatchingOnesApply()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        var shaderA = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("value", ShaderUniformType.Float, 0, new Vector4(0.75f, 0f, 0f, 0f)),
            ],
        };
        var shaderB = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform("value", ShaderUniformType.Float, 0, new Vector4(0.1f, 0f, 0f, 0f)),
            ],
        };
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = FontTestSupport.CreateFont();
        font.Text = "A";
        font.Shader = shaderA;
        font.SetUniform("value", 0.5f);

        font.Shader = shaderB;
        Assert.Equal(0.5f, FontTestSupport.Capture(engine).Fonts[0].Uniform0.X);

        font.Shader = shaderA;
        Assert.Equal(0.5f, FontTestSupport.Capture(engine).Fonts[0].Uniform0.X);
    }

    [Fact]
    public void MalformedFrozenFontDataNeverThrowsFromBoundsOrCapture()
    {
        GameEngine<GameLayer> engine = FontTestSupport.CreateEngine();
        FontAsset asset = FontTestSupport.CreateFont();
        asset.Glyphs = null!;
        asset.Kerning = null!;
        asset.Ascent = float.NaN;
        IFontRender<GameLayer> font = engine.CreateFontRender(engine.Root);
        font.Font = asset;
        font.Text = "AB";

        Assert.Equal(Bounds.Empty, font.Bounds);
        FrameSnapshot snapshot = FontTestSupport.Capture(engine);
        Assert.Equal(1, snapshot.FontCount);
        Assert.Same(FontLayoutData.Empty, snapshot.Fonts[0].Layout);
        Assert.Equal(Bounds.Empty, snapshot.Fonts[0].Bounds);
    }

    [Fact]
    public void FontBoundsHelperReturnsEmptyForNoLayoutOrNoQuads()
    {
        Assert.Equal(Bounds.Empty, FontBounds.CreateWorldBounds(null, Matrix3x2.Identity));
        Assert.Equal(Bounds.Empty, FontBounds.CreateWorldBounds(FontLayoutData.Empty, Matrix3x2.Identity));
    }

    private static FontLayoutData Layout(GameEngine<GameLayer> engine)
        => Assert.IsType<FontLayoutData>(FontTestSupport.Capture(engine).Fonts[0].Layout);

    private static void AssertBounds(Bounds bounds, Vector2 min, Vector2 max)
    {
        Assert.False(bounds.IsEmpty);
        TestAssertions.Close(min, bounds.Min);
        TestAssertions.Close(max, bounds.Max);
    }
}
