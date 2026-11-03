using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Scene;

/// <summary>
/// Deterministic tests for the sprite-side custom shader contract: frozen metadata capture, strict
/// instance-uniform validation, overrides over defaults, and additional texture capture.
/// </summary>
public sealed class SpriteShaderTests
{
    [Fact]
    public void CaptureCarriesTheFrozenShaderMetadataAndDefaultBlock()
    {
        var engine = TestEngineFactory.Create();
        ShaderAsset asset = AllTypesShader();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = asset;

        SpriteSnapshot captured = Capture(engine).Sprites[0];

        Assert.Same(ShaderRenderData.Freeze(asset), captured.ShaderData);
        Assert.Equal(new Vector4(0.5f, 1f, 2f, 3f), captured.Uniform0);
        Assert.Equal(new Vector4(4f, 5f, 6f, 7f), captured.Uniform1);
        Assert.Equal(new Vector4(8f, 9f, 1f, 1f), captured.Uniform2);
        Assert.Equal(new Vector4(1f, 1f, 0f, 0f), captured.Uniform3);
        Assert.Equal(0, captured.AdditionalTextureCount);
        Assert.False(captured.ExtraTexture0.HasValue);
    }

    [Fact]
    public void MutatingTheShaderAssetAfterAssignmentDoesNotChangeTheCapture()
    {
        var engine = TestEngineFactory.Create();
        ShaderAsset asset = AllTypesShader();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = asset;

        asset.EntryPoint = "other";
        asset.LayoutVersion = 99;
        asset.InstanceUniforms[0] = new ShaderUniform("f", ShaderUniformType.Float, 0, Vector4.Zero);
        asset.InstanceUniforms = [new ShaderUniform("f", ShaderUniformType.Float, 0, Vector4.One)];
        asset.TextureSlots[0] = new ShaderTextureSlot("other");

        SpriteSnapshot captured = Capture(engine).Sprites[0];

        Assert.Same(ShaderRenderData.Freeze(asset), captured.ShaderData);
        Assert.Equal("main", captured.ShaderData!.EntryPoint);
        Assert.Equal(0u, captured.ShaderData.LayoutVersion);
        Assert.Equal(new Vector4(0.5f, 1f, 2f, 3f), captured.Uniform0);
    }

    [Fact]
    public void SetUniformStoresEveryOverloadAndCaptureAppliesThem()
    {
        var engine = TestEngineFactory.Create();
        ShaderAsset asset = AllTypesShader();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = asset;

        sprite.SetUniform("f", 10f);
        sprite.SetUniform("v2", new Vector2(20f, 21f));
        sprite.SetUniform("v3", new Vector3(30f, 31f, 32f));
        sprite.SetUniform("v4", new Vector4(40f, 41f, 42f, 43f));
        sprite.SetUniform("c", new Color(0.1f, 0.2f, 0.3f, 0.4f));

        SpriteSnapshot captured = Capture(engine).Sprites[0];

        Assert.Equal(new Vector4(10f, 20f, 21f, 30f), captured.Uniform0);
        Assert.Equal(new Vector4(31f, 32f, 40f, 41f), captured.Uniform1);
        Assert.Equal(new Vector4(42f, 43f, 0.1f, 0.2f), captured.Uniform2);
        Assert.Equal(new Vector4(0.3f, 0.4f, 0f, 0f), captured.Uniform3);
    }

    [Fact]
    public void SetUniformWithoutAShaderThrows()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => sprite.SetUniform("value", 1f));

        Assert.Contains("value", error.Message, StringComparison.Ordinal);
        Assert.Contains("Shader", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetUniformWithAnUnknownNameThrows()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = AllTypesShader();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => sprite.SetUniform("missing", 1f));

        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetUniformWithTheWrongTypeThrowsAndNamesTheExpectedType()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = AllTypesShader();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => sprite.SetUniform("v2", 1f));

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
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = AllTypesShader();

        Assert.ThrowsAny<ArgumentException>(() => sprite.SetUniform(name!, 1f));
    }

    [Fact]
    public void ResetUniformsRestoresTheShaderDefaults()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = AllTypesShader();
        sprite.SetUniform("f", 10f);
        Assert.Equal(10f, Capture(engine).Sprites[0].Uniform0.X);

        sprite.ResetUniforms();

        Assert.Equal(0.5f, Capture(engine).Sprites[0].Uniform0.X);
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
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.Shader = floatShader;
        sprite.SetUniform("value", 5f);

        sprite.Shader = vectorShader;
        Assert.Equal(Vector4.Zero, Capture(engine).Sprites[0].Uniform0);

        sprite.Shader = floatShader;
        Assert.Equal(new Vector4(5f, 0f, 0f, 0f), Capture(engine).Sprites[0].Uniform0);
    }

    [Fact]
    public void AdditionalTexturesSetterClonesTheArrayAndCaptureKeepsFourPlusTheTotal()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        AssetRef<TextureDescriptor>[] textures =
        [
            "textures/0",
            "textures/1",
            "textures/2",
            "textures/3",
            "textures/4",
            "textures/5",
        ];

        sprite.AdditionalTextures = textures;
        textures[0] = "textures/changed";

        AssetRef<TextureDescriptor>[] stored = sprite.AdditionalTextures!;
        Assert.Equal(6, stored.Length);
        Assert.Equal("textures/0", stored[0].Path);
        stored[1] = "textures/changed";

        SpriteSnapshot captured = Capture(engine).Sprites[0];

        Assert.Equal(6, captured.AdditionalTextureCount);
        Assert.Equal("textures/0", captured.ExtraTexture0.Path);
        Assert.Equal("textures/1", captured.ExtraTexture1.Path);
        Assert.Equal("textures/2", captured.ExtraTexture2.Path);
        Assert.Equal("textures/3", captured.ExtraTexture3.Path);
        Assert.Equal("textures/1", sprite.AdditionalTextures![1].Path);
    }

    [Fact]
    public void AssigningNullAdditionalTexturesClearsThem()
    {
        var engine = TestEngineFactory.Create();
        ISpriteRender<GameLayer> sprite = engine.CreateSpriteRender(engine.Root);
        sprite.AdditionalTextures = ["textures/0"];

        sprite.AdditionalTextures = null;

        Assert.Null(sprite.AdditionalTextures);
        SpriteSnapshot captured = Capture(engine).Sprites[0];
        Assert.Equal(0, captured.AdditionalTextureCount);
        Assert.False(captured.ExtraTexture0.HasValue);
    }

    [Fact]
    public void CaptureWithoutAShaderLeavesSafeDefaults()
    {
        var engine = TestEngineFactory.Create();
        engine.CreateSpriteRender(engine.Root);

        SpriteSnapshot captured = Capture(engine).Sprites[0];

        Assert.Null(captured.ShaderData);
        Assert.Equal(Vector4.Zero, captured.Uniform0);
        Assert.Equal(Vector4.Zero, captured.Uniform1);
        Assert.Equal(Vector4.Zero, captured.Uniform2);
        Assert.Equal(Vector4.Zero, captured.Uniform3);
        Assert.Equal(0, captured.AdditionalTextureCount);
    }

    [Fact]
    public void TheLegacySevenArgumentSnapshotConstructorKeepsSafeDefaults()
    {
        var captured = new SpriteSnapshot(
            null,
            Matrix3x2.Identity,
            Color.White,
            1u,
            0,
            FlipX: false,
            FlipY: false);

        Assert.Null(captured.ShaderData);
        Assert.Equal(Vector4.Zero, captured.Uniform0);
        Assert.Equal(Vector4.Zero, captured.Uniform1);
        Assert.Equal(Vector4.Zero, captured.Uniform2);
        Assert.Equal(Vector4.Zero, captured.Uniform3);
        Assert.False(captured.ExtraTexture0.HasValue);
        Assert.False(captured.ExtraTexture1.HasValue);
        Assert.False(captured.ExtraTexture2.HasValue);
        Assert.False(captured.ExtraTexture3.HasValue);
        Assert.Equal(0, captured.AdditionalTextureCount);
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
        TextureSlots =
        [
            new ShaderTextureSlot("noise"),
        ],
    };

    private static FrameSnapshot Capture(GameEngine<GameLayer> engine)
    {
        var snapshot = new FrameSnapshot();
        engine.CaptureSnapshot(snapshot, sequence: 1);
        return snapshot;
    }
}
