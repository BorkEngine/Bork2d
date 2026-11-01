using System.Numerics;
using System.Runtime.InteropServices;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the 96-byte instance ABI, the mapped-memory writer, and the instance
/// builder (M8 §6.6).
/// </summary>
public sealed class SpriteInstanceTests
{
    [Fact]
    public void RecordHasTheApproved96ByteLayout()
    {
        Assert.Equal(96, Marshal.SizeOf<SpriteInstance>());
        Assert.Equal(96, SpriteInstance.SizeInBytes);
        Assert.Equal(0, OffsetOf(nameof(SpriteInstance.Transform0)));
        Assert.Equal(16, OffsetOf(nameof(SpriteInstance.Transform1)));
        Assert.Equal(32, OffsetOf(nameof(SpriteInstance.Uv0)));
        Assert.Equal(48, OffsetOf(nameof(SpriteInstance.Uv1)));
        Assert.Equal(64, OffsetOf(nameof(SpriteInstance.Tint)));
        Assert.Equal(80, OffsetOf(nameof(SpriteInstance.Misc)));
        Assert.Equal(0, SpriteInstance.Transform0Offset);
        Assert.Equal(16, SpriteInstance.Transform1Offset);
        Assert.Equal(32, SpriteInstance.Uv0Offset);
        Assert.Equal(48, SpriteInstance.Uv1Offset);
        Assert.Equal(64, SpriteInstance.TintOffset);
        Assert.Equal(80, SpriteInstance.MiscOffset);
    }

    [Fact]
    public void WriterWritesTheRecordDirectlyIntoMappedMemory()
    {
        var mapped = new byte[SpriteInstance.SizeInBytes];
        var instance = new SpriteInstance
        {
            Transform0 = new Vector4(1f, 2f, 3f, 4f),
            Transform1 = new Vector4(5f, 6f, 7f, 8f),
            Uv0 = new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
            Uv1 = new Vector4(0.5f, 0.6f, 0.7f, 0.8f),
            Tint = new Vector4(0.9f, 0.8f, 0.7f, 0.6f),
            Misc = new Vector4(0.5f, 0.5f, 7f, 0f),
        };

        SpriteInstanceWriter.Write(mapped, instance);

        SpriteInstance written = MemoryMarshal.Read<SpriteInstance>(mapped);
        Assert.Equal(instance.Transform0, written.Transform0);
        Assert.Equal(instance.Transform1, written.Transform1);
        Assert.Equal(instance.Uv0, written.Uv0);
        Assert.Equal(instance.Uv1, written.Uv1);
        Assert.Equal(instance.Tint, written.Tint);
        Assert.Equal(instance.Misc, written.Misc);
        Assert.Equal(0f, written.Misc.W);
    }

    [Fact]
    public void WriterRejectsAShortDestination()
    {
        var mapped = new byte[SpriteInstance.SizeInBytes - 1];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpriteInstanceWriter.Write(mapped, default));
    }

    [Fact]
    public void BuilderWritesTheApprovedFieldsInOrder()
    {
        SpriteAsset asset = Asset(
            uvs:
            [
                new Vector2(0.25f, 0.75f),
                new Vector2(0.5f, 0.75f),
                new Vector2(0.5f, 1f),
                new Vector2(0.25f, 1f),
            ],
            scale: new Vector2(10f),
            pivot: new Vector2(0.25f, 0.75f));
        var world = Matrix3x2.CreateScale(2f, 3f) * Matrix3x2.CreateTranslation(10f, 20f);
        SpriteSnapshot snapshot = Snapshot(
            asset,
            world,
            tint: new Color(0.1f, 0.2f, 0.3f, 0.4f),
            layer: 1,
            zIndex: 2,
            flipX: true,
            flipY: false);
        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: true, flipY: false);

        SpriteInstance instance = SpriteInstanceBuilder.Create(snapshot, quad, textureIndex: 9);

        Assert.Equal(new Vector4(2f, 0f, 0f, 3f), instance.Transform0);
        Assert.Equal(new Vector4(10f, 20f, quad.SizeX, quad.SizeY), instance.Transform1);
        Assert.Equal(new Vector4(0.25f, 0.75f, 0.5f, 0.75f), instance.Uv0);
        Assert.Equal(new Vector4(0.5f, 1f, 0.25f, 1f), instance.Uv1);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), instance.Tint);
        Assert.Equal(new Vector4(0.25f, 0.75f, 9f, 0f), instance.Misc);
        Assert.Equal(0f, instance.Misc.W);
    }

    [Fact]
    public void FlagsStayZeroForBothFlipAxes()
    {
        SpriteAsset asset = Asset(scale: new Vector2(2f));
        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: true, flipY: true);
        SpriteInstance instance = SpriteInstanceBuilder.Create(
            Snapshot(asset, Matrix3x2.Identity),
            quad,
            0);

        Assert.Equal(0f, instance.Misc.W);
        Assert.Equal(-2f, instance.Transform1.Z);
        Assert.Equal(-2f, instance.Transform1.W);
    }

    [Fact]
    public void TransformCarriesTheWorldMatrixIncludingNegativeLocalScale()
    {
        SpriteAsset asset = Asset();
        var world = Matrix3x2.CreateScale(-1f, 2f) * Matrix3x2.CreateTranslation(3f, 4f);
        SpriteQuad quad = SpriteQuad.Resolve(SpriteRenderData.Freeze(asset), flipX: false, flipY: false);

        SpriteInstance instance = SpriteInstanceBuilder.Create(
            Snapshot(asset, world),
            quad,
            0);

        Assert.Equal(-1f, instance.Transform0.X);
        Assert.Equal(2f, instance.Transform0.W);
        Assert.Equal(quad.SizeX, instance.Transform1.Z);
        Assert.True(instance.Transform1.Z > 0f);
    }

    [Fact]
    public void BuilderRejectsNullSpriteAndMissingUvs()
    {
        Assert.Throws<ArgumentException>(
            () => SpriteInstanceBuilder.Create(default, default, 0));

        SpriteAsset asset = Asset();
        asset.Uvs = null;
        Assert.Throws<ArgumentException>(
            () => SpriteInstanceBuilder.Create(Snapshot(asset, Matrix3x2.Identity), default, 0));
    }

    private static int OffsetOf(string field)
        => (int)Marshal.OffsetOf<SpriteInstance>(field);

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
        SpriteAsset? asset,
        Matrix3x2 world,
        Color? tint = null,
        uint layer = 1,
        int zIndex = 0,
        bool flipX = false,
        bool flipY = false)
    {
        return new SpriteSnapshot(
            SpriteRenderData.FreezeOrNull(asset),
            world,
            tint ?? Color.White,
            layer,
            zIndex,
            flipX,
            flipY);
    }
}
