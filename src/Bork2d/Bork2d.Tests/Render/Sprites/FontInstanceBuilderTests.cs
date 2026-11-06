using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the font glyph instance builder: it maps one laid-out glyph quad onto the
/// shared 176-byte sprite instance ABI (top-left pivot, world top-left translation, UV order, tint,
/// atlas index, and the seeded uniform lanes) without a dedicated glyph buffer or vertex stage.
/// </summary>
public sealed class FontInstanceBuilderTests
{
    [Fact]
    public void TheGlyphInstanceUsesTheSpriteAbiFieldOffsets()
    {
        Assert.Equal(176, Marshal.SizeOf<SpriteInstance>());
        Assert.Equal(176, SpriteInstance.SizeInBytes);
        Assert.Equal(0, OffsetOf(nameof(SpriteInstance.Transform0)));
        Assert.Equal(16, OffsetOf(nameof(SpriteInstance.Transform1)));
        Assert.Equal(32, OffsetOf(nameof(SpriteInstance.Uv0)));
        Assert.Equal(48, OffsetOf(nameof(SpriteInstance.Uv1)));
        Assert.Equal(64, OffsetOf(nameof(SpriteInstance.Tint)));
        Assert.Equal(80, OffsetOf(nameof(SpriteInstance.Misc)));
        Assert.Equal(96, OffsetOf(nameof(SpriteInstance.ExtraTextureIndices)));
        Assert.Equal(112, OffsetOf(nameof(SpriteInstance.InstanceParams0)));
        Assert.Equal(128, OffsetOf(nameof(SpriteInstance.InstanceParams1)));
        Assert.Equal(144, OffsetOf(nameof(SpriteInstance.InstanceParams2)));
        Assert.Equal(160, OffsetOf(nameof(SpriteInstance.InstanceParams3)));
    }

    [Fact]
    public void TheBuilderMapsTheGlyphQuadAndFontStateOntoTheInstanceRecord()
    {
        FontAsset asset = FontTestSupport.CreateFont();
        FontRenderData data = FontRenderData.Freeze(asset);
        var quad = new FontGlyphQuad(
            Min: new Vector2(1f, 2f),
            Size: new Vector2(3f, 4f),
            Uv: new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        var world = new Matrix3x2(2f, 0.5f, -0.5f, 3f, 10f, 20f);
        var font = new FontSnapshot(
            data,
            FontLayoutData.Empty,
            world,
            new Color(0.2f, 0.3f, 0.4f, 0.5f),
            Layer: 1u,
            ZIndex: 7)
        {
            ShaderData = null,
            Uniform0 = new Vector4(1f, 2f, 3f, 4f),
            Uniform1 = new Vector4(5f, 6f, 7f, 8f),
            Uniform2 = new Vector4(9f, 10f, 11f, 12f),
            Uniform3 = new Vector4(13f, 14f, 15f, 16f),
        };

        SpriteInstance instance = FontInstanceBuilder.Create(
            font,
            quad,
            atlasIndex: 5,
            params0: new Vector4(21f, 22f, 23f, 24f),
            params1: new Vector4(25f, 26f, 27f, 28f),
            params2: new Vector4(29f, 30f, 31f, 32f),
            params3: new Vector4(33f, 34f, 35f, 36f));

        // The quad's local top-left is (Min.X, Min.Y + Size.Y) and becomes the world translation.
        Vector2 worldTopLeft = Vector2.Transform(
            new Vector2(quad.Min.X, quad.Min.Y + quad.Size.Y),
            world);
        Assert.Equal(new Vector4(world.M11, world.M12, world.M21, world.M22), instance.Transform0);
        Assert.Equal(worldTopLeft.X, instance.Transform1.X, 5);
        Assert.Equal(worldTopLeft.Y, instance.Transform1.Y, 5);
        Assert.Equal(quad.Size.X, instance.Transform1.Z);
        Assert.Equal(quad.Size.Y, instance.Transform1.W);

        // The sprite stage's TL/TR/BL/BR corner order maps UV0 to TL,TR and UV1 to BR,BL.
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.2f), instance.Uv0);
        Assert.Equal(new Vector4(0.3f, 0.4f, 0.1f, 0.4f), instance.Uv1);

        Assert.Equal(new Vector4(0.2f, 0.3f, 0.4f, 0.5f), instance.Tint);
        Assert.Equal(new Vector4(0f, 0f, 5f, 0f), instance.Misc);
        Assert.Equal(Vector4.Zero, instance.ExtraTextureIndices);
        Assert.Equal(new Vector4(21f, 22f, 23f, 24f), instance.InstanceParams0);
        Assert.Equal(new Vector4(25f, 26f, 27f, 28f), instance.InstanceParams1);
        Assert.Equal(new Vector4(29f, 30f, 31f, 32f), instance.InstanceParams2);
        Assert.Equal(new Vector4(33f, 34f, 35f, 36f), instance.InstanceParams3);
    }

    [Fact]
    public void TheBuilderKeepsThePlaceholderAtlasIndexAndZeroFlags()
    {
        FontAsset asset = FontTestSupport.CreateFont();
        var font = new FontSnapshot(
            FontRenderData.Freeze(asset),
            FontLayoutData.Empty,
            Matrix3x2.Identity,
            Color.White,
            Layer: 1u,
            ZIndex: 0);
        var quad = new FontGlyphQuad(Vector2.Zero, new Vector2(1f), Vector4.Zero);

        SpriteInstance instance = FontInstanceBuilder.Create(
            font,
            quad,
            atlasIndex: 0,
            params0: Vector4.Zero,
            params1: Vector4.Zero,
            params2: Vector4.Zero,
            params3: Vector4.Zero);

        Assert.Equal(0f, instance.Misc.Z);
        Assert.Equal(0f, instance.Misc.W);
        Assert.Equal(Vector4.Zero, instance.ExtraTextureIndices);
    }

    private static int OffsetOf(string field)
        => (int)Marshal.OffsetOf<SpriteInstance>(field);
}
