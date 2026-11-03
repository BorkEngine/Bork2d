using System.Numerics;
using Bork2d.Engine.Assets;
using Xunit;

namespace Bork2d.Tests.Assets.Sprites;

/// <summary>
/// Deterministic tests for the frozen shader metadata: idempotent freezing, cloned declarations,
/// the precomputed default instance block, ordinal lookups, and the tolerant lane helpers.
/// </summary>
public sealed class ShaderRenderDataTests
{
    [Fact]
    public void FreezingTheSameAssetReturnsTheSameInstance()
    {
        ShaderAsset asset = CreateAsset();

        ShaderRenderData first = ShaderRenderData.Freeze(asset);
        ShaderRenderData second = ShaderRenderData.Freeze(asset);

        Assert.Same(first, second);
        Assert.Equal("shaders/custom", first.SpirV.Path);
        Assert.Equal(1u, first.LayoutVersion);
        Assert.Equal("fragment_main", first.EntryPoint);
        Assert.Equal(asset.InstanceUniforms, first.InstanceUniforms);
        Assert.Equal(asset.GlobalUniforms, first.GlobalUniforms);
        Assert.Equal(asset.TextureSlots, first.TextureSlots);
    }

    [Fact]
    public void FrozenDeclarationsAreClonesOfTheAssetDeclarations()
    {
        ShaderAsset asset = CreateAsset();
        ShaderRenderData frozen = ShaderRenderData.Freeze(asset);

        asset.SpirV = "shaders/other";
        asset.LayoutVersion = 2;
        asset.EntryPoint = "other_main";
        asset.InstanceUniforms[0] = new ShaderUniform(
            "tint",
            ShaderUniformType.Vector4,
            12,
            Vector4.Zero);
        asset.InstanceUniforms = [new ShaderUniform("replacement", ShaderUniformType.Float, 0, Vector4.One)];
        asset.GlobalUniforms[0] = new ShaderUniform("clock", ShaderUniformType.Float, 4, Vector4.One);
        asset.TextureSlots[0] = new ShaderTextureSlot("other_slot");

        Assert.Equal("shaders/custom", frozen.SpirV.Path);
        Assert.Equal(1u, frozen.LayoutVersion);
        Assert.Equal("fragment_main", frozen.EntryPoint);
        Assert.Equal(3, frozen.InstanceUniforms.Length);
        Assert.Equal("tint", frozen.InstanceUniforms[0].Name);
        Assert.Equal(0, frozen.InstanceUniforms[0].Offset);
        Assert.Equal("time", frozen.GlobalUniforms[0].Name);
        Assert.Equal("noise", frozen.TextureSlots[0].Name);

        // The precomputed default block is built from the frozen declarations, not the live ones.
        ReadOnlySpan<Vector4> block = frozen.DefaultInstanceUniformBlock;
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), block[0]);
        Assert.Equal(new Vector4(3f, 4f, 0.5f, 0f), block[1]);
    }

    [Fact]
    public void FreezingNullDeclarationArraysProducesEmptyFrozenArrays()
    {
        var asset = new ShaderAsset
        {
            InstanceUniforms = null!,
            GlobalUniforms = null!,
            TextureSlots = null!,
            EntryPoint = null!,
        };

        ShaderRenderData frozen = ShaderRenderData.Freeze(asset);

        Assert.Empty(frozen.InstanceUniforms);
        Assert.Empty(frozen.GlobalUniforms);
        Assert.Empty(frozen.TextureSlots);
        Assert.Equal(string.Empty, frozen.EntryPoint);
        Assert.Equal(Vector4.Zero, frozen.DefaultInstanceUniformBlock[0]);
        Assert.Equal(ShaderLayout.InstanceBlockVectorCount, frozen.DefaultInstanceUniformBlock.Length);
    }

    [Fact]
    public void DefaultInstanceBlockUsesTheDeclaredOffsetsAndDefaults()
    {
        ShaderRenderData frozen = ShaderRenderData.Freeze(CreateAsset());

        ReadOnlySpan<Vector4> block = frozen.DefaultInstanceUniformBlock;

        Assert.Equal(ShaderLayout.InstanceBlockVectorCount, block.Length);
        Assert.Equal(new Vector4(1f, 1f, 1f, 1f), block[0]);
        Assert.Equal(new Vector4(3f, 4f, 0.5f, 0f), block[1]);
        Assert.Equal(Vector4.Zero, block[2]);
        Assert.Equal(Vector4.Zero, block[3]);
    }

    [Fact]
    public void LookupIsExactAndOrdinal()
    {
        ShaderRenderData frozen = ShaderRenderData.Freeze(CreateAsset());

        Assert.True(frozen.TryGetInstanceUniform("tint", out ShaderUniform tint));
        Assert.Equal(ShaderUniformType.Color, tint.Type);
        Assert.True(frozen.TryGetInstanceUniform("intensity", out _));
        Assert.False(frozen.TryGetInstanceUniform("Tint", out _));
        Assert.False(frozen.TryGetInstanceUniform("missing", out _));
        Assert.False(frozen.TryGetInstanceUniform(null, out _));

        Assert.True(frozen.TryGetGlobalUniform("time", out ShaderUniform time));
        Assert.Equal(ShaderUniformType.Float, time.Type);
        Assert.False(frozen.TryGetGlobalUniform(null, out _));
    }

    [Fact]
    public void MalformedDeclarationsAreToleratedAndSafelyIgnored()
    {
        var asset = new ShaderAsset
        {
            InstanceUniforms =
            [
                new ShaderUniform(null!, ShaderUniformType.Float, 0, Vector4.One),
                new ShaderUniform("duplicate", ShaderUniformType.Float, 1, new Vector4(2f)),
                new ShaderUniform("duplicate", ShaderUniformType.Vector2, 2, new Vector4(3f)),
                new ShaderUniform("negative", ShaderUniformType.Vector4, -4, Vector4.One),
                new ShaderUniform("past_end", ShaderUniformType.Vector2, 15, Vector4.One),
                new ShaderUniform("unknown", (ShaderUniformType)99, 0, Vector4.One),
            ],
        };

        ShaderRenderData frozen = ShaderRenderData.Freeze(asset);

        Assert.Equal(6, frozen.InstanceUniforms.Length);
        Assert.True(frozen.TryGetInstanceUniform("duplicate", out ShaderUniform duplicate));
        Assert.Equal(ShaderUniformType.Float, duplicate.Type);
        Assert.False(frozen.TryGetInstanceUniform(null, out _));

        // Only the first duplicate's default lands; the invalid and unknown declarations write
        // nothing, so no lane is corrupted.
        ReadOnlySpan<Vector4> block = frozen.DefaultInstanceUniformBlock;
        Assert.Equal(new Vector4(0f, 2f, 0f, 0f), block[0]);
        Assert.Equal(Vector4.Zero, block[1]);
        Assert.Equal(Vector4.Zero, block[2]);
        Assert.Equal(Vector4.Zero, block[3]);
    }

    [Fact]
    public void LaneHelpersIgnoreOutOfRangeLanes()
    {
        Assert.False(ShaderLayout.TryReadComponent(ReadOnlySpan<Vector4>.Empty, 0, out float empty));
        Assert.Equal(0f, empty);

        Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
        Assert.False(ShaderLayout.TryWriteComponent(block, -1, 1f));
        Assert.False(ShaderLayout.TryWriteComponent(block, 16, 1f));
        Assert.True(ShaderLayout.TryWriteComponent(block, 15, 2f));
        Assert.Equal(2f, block[3].W);
        Assert.True(ShaderLayout.TryReadComponent(block, 15, out float read));
        Assert.Equal(2f, read);
    }

    [Fact]
    public void UniformHelpersRoundTripOnlyTheCoveredComponents()
    {
        Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
        var uniform = new ShaderUniform("offset", ShaderUniformType.Vector2, 4, Vector4.Zero);

        ShaderLayout.WriteUniform(block, in uniform, new Vector4(7f, 8f, 9f, 10f));

        Assert.Equal(new Vector4(7f, 8f, 0f, 0f), block[1]);
        Assert.Equal(new Vector4(7f, 8f, 0f, 0f), ShaderLayout.ReadUniform(block, in uniform));
    }

    [Fact]
    public void OutOfRangeUniformWritesNothing()
    {
        Span<Vector4> block = stackalloc Vector4[ShaderLayout.InstanceBlockVectorCount];
        var uniform = new ShaderUniform("bad", ShaderUniformType.Vector4, 14, Vector4.One);

        ShaderLayout.WriteUniform(block, in uniform, Vector4.One);

        Assert.Equal(Vector4.Zero, block[3]);
        Assert.Equal(Vector4.Zero, ShaderLayout.ReadUniform(block, in uniform));

        var negative = new ShaderUniform("negative", ShaderUniformType.Vector2, -1, Vector4.Zero);
        ShaderLayout.WriteUniform(block, in negative, Vector4.One);
        Assert.Equal(Vector4.Zero, ShaderLayout.ReadUniform(block, in negative));
        Assert.Equal(Vector4.Zero, block[0]);
    }

    private static ShaderAsset CreateAsset() => new()
    {
        SpirV = "shaders/custom",
        LayoutVersion = 1,
        EntryPoint = "fragment_main",
        InstanceUniforms =
        [
            new ShaderUniform("tint", ShaderUniformType.Color, 0, new Vector4(1f, 1f, 1f, 1f)),
            new ShaderUniform("offset", ShaderUniformType.Vector2, 4, new Vector4(3f, 4f, 0f, 0f)),
            new ShaderUniform("intensity", ShaderUniformType.Float, 6, new Vector4(0.5f, 0f, 0f, 0f)),
        ],
        GlobalUniforms =
        [
            new ShaderUniform("time", ShaderUniformType.Float, 0, Vector4.Zero),
        ],
        TextureSlots =
        [
            new ShaderTextureSlot("noise"),
        ],
    };
}
