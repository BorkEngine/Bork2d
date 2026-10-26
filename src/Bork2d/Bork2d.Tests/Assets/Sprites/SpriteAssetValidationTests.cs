using System.Numerics;
using Bork2d.Api.Assets;
using Bork2d.Assets.Descriptors;
using Bork2d.Assets.Pack;
using Bork2d.Tests.Assets.Service;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Sprites;

public sealed class SpriteAssetValidationTests
{
    [Fact]
    public async Task ValidQuadRoundTripsTheGeneratedShape()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>(
            SpriteTestSupport.SpritePath);

        Assert.Equal(SpriteTestSupport.TexturePath, sprite.Texture);
        Assert.NotNull(sprite.Uvs);
        Assert.Equal(4, sprite.Uvs.Length);
        Assert.Equal(new Vector2(0f, 0f), sprite.Uvs[0]);
        Assert.Equal(new Vector2(1f, 0f), sprite.Uvs[1]);
        Assert.Equal(new Vector2(1f, 1f), sprite.Uvs[2]);
        Assert.Equal(new Vector2(0f, 1f), sprite.Uvs[3]);
        Assert.Equal(16f, sprite.Scale);
        Assert.Equal(new Vector2(0.5f, 0.5f), sprite.Pivot);
        Assert.Equal(SpriteTopology.Quad, sprite.Topology);
        Assert.Null(sprite.Indices);
        Assert.Equal(0, host.Service.SpriteResourceCount);
        Assert.Equal(0, host.Service.TextureResourceCount);
    }

    [Fact]
    public async Task ValidTriangleStripRoundTripsItsIndices()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(
            directory,
            sprite: SpriteTestSupport.TriangleStrip());

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>(
            SpriteTestSupport.SpritePath);

        Assert.Equal(SpriteTopology.TriangleStrip, sprite.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2, 3 }, sprite.Indices);
    }

    [Fact]
    public async Task ValidTrianglesRoundTripsItsIndices()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(
            directory,
            sprite: SpriteTestSupport.Triangles());

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SpriteAsset sprite = await host.Service.LoadAsset<SpriteAsset>(
            SpriteTestSupport.SpritePath);

        Assert.Equal(SpriteTopology.Triangles, sprite.Topology);
        Assert.Equal(new ushort[] { 0, 1, 2 }, sprite.Indices);
    }

    [Fact]
    public void PivotDefaultsToTheCenter()
    {
        var sprite = new SpriteAsset();

        Assert.Equal(new Vector2(0.5f, 0.5f), sprite.Pivot);
        Assert.Equal(SpriteTopology.Quad, sprite.Topology);
    }

    [Fact]
    public void SpriteUsesPropertyNameKeysAndHasNoVerticesField()
    {
        byte[] payload = PackSerialization.SerializeAsset(SpriteTestSupport.Quad());

        Assert.True(payload.AsSpan().IndexOf("Texture"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Uvs"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Scale"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Pivot"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Topology"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Indices"u8) >= 0);
        Assert.True(payload.AsSpan().IndexOf("Vertices"u8) < 0);
    }

    [Fact]
    public void TopologyIsSerializedAsAnInteger()
    {
        byte[] payload = PackSerialization.SerializeAsset(SpriteTestSupport.TriangleStrip());

        int keyIndex = payload.AsSpan().IndexOf("Topology"u8);
        Assert.True(keyIndex >= 0);

        // The MessagePack string key is followed immediately by the integer value token; a string
        // encoding would start with a string header instead.
        Assert.Equal(
            (byte)SpriteTopology.TriangleStrip,
            payload[keyIndex + "Topology"u8.Length]);
    }

    [Fact]
    public async Task ThreeUvsAreInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs = sprite.Uvs![..3];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task FiveUvsAreInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs = [.. sprite.Uvs!, new Vector2(0.5f, 0.5f)];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NullUvsAreInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs = null;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NonFiniteUvIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs![1] = new Vector2(float.NaN, 0f);

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task InfiniteUvIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs![1] = new Vector2(float.PositiveInfinity, 0f);

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task UvOutsideTheNormalizedRangeIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs![1] = new Vector2(1.5f, 0f);

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task UvsWithoutHorizontalExtentAreInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs =
        [
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
        ];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task UvsWithoutVerticalExtentAreInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Uvs =
        [
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(0f, 0.5f),
        ];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task ZeroScaleIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Scale = 0f;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NegativeScaleIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Scale = -1f;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NonFiniteScaleIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Scale = float.NaN;

        await AssertInvalidAsync(sprite);

        sprite.Scale = float.PositiveInfinity;
        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task PivotOutsideTheNormalizedRangeIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Pivot = new Vector2(-0.1f, 0.5f);

        await AssertInvalidAsync(sprite);

        sprite.Pivot = new Vector2(0.5f, 1.1f);
        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NonFinitePivotIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Pivot = new Vector2(float.NaN, 0.5f);

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task UnknownTopologyIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Topology = (SpriteTopology)3;

        await AssertInvalidAsync(sprite);

        sprite.Topology = (SpriteTopology)(-1);
        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task QuadWithIndicesIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Indices = [0, 1, 2, 3];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task TriangleStripWithoutIndicesIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.TriangleStrip();
        sprite.Indices = null;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task TrianglesWithoutIndicesIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Triangles();
        sprite.Indices = null;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task IndexAboveThreeIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.TriangleStrip();
        sprite.Indices = [0, 1, 4, 3];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task FewerThanThreeIndicesIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.TriangleStrip();
        sprite.Indices = [0, 1];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task TrianglesLengthNotAMultipleOfThreeIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Triangles();
        sprite.Indices = [0, 1, 2, 3];

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NullTexturePathIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Texture = null;

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task NonCanonicalTexturePathIsInvalidData()
    {
        SpriteAsset sprite = SpriteTestSupport.Quad();
        sprite.Texture = "Textures/player";

        await AssertInvalidAsync(sprite);
    }

    [Fact]
    public async Task MissingTextureDescriptorIsInvalidData()
    {
        AssetException exception = await AssertInvalidAsync(
            SpriteTestSupport.Quad(),
            includeDescriptor: false);

        Assert.Contains(SpriteTestSupport.TexturePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawTextureDescriptorIsInvalidData()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw(SpriteTestSupport.TexturePath, [1, 2, 3]);
                builder.AddRaw(
                    SpriteTestSupport.PayloadPath,
                    SpriteTestSupport.Payload(SpriteTestSupport.Descriptor()));
                builder.AddMessagePack(
                    SpriteTestSupport.SpritePath,
                    SpriteTestSupport.Quad());
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<SpriteAsset>(SpriteTestSupport.SpritePath));

        Assert.Equal(AssetErrorKind.InvalidData, exception.Kind);
        Assert.Equal(SpriteTestSupport.SpritePath, exception.AssetPath);
    }

    [Fact]
    public async Task InvalidTextureDescriptorIsInvalidData()
    {
        TextureDescriptor descriptor = SpriteTestSupport.Descriptor();
        descriptor.Width = 0;

        AssetException exception = await AssertInvalidAsync(
            SpriteTestSupport.Quad(),
            descriptor: descriptor);

        Assert.Contains(SpriteTestSupport.TexturePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTexturePayloadIsInvalidData()
    {
        await AssertInvalidAsync(SpriteTestSupport.Quad(), includePayload: false);
    }

    [Fact]
    public async Task TexturePayloadSizeMismatchIsInvalidData()
    {
        await AssertInvalidAsync(
            SpriteTestSupport.Quad(),
            payload: new byte[15]);
    }

    [Fact]
    public async Task MissingTopLevelSpriteFaultsWithNotFound()
    {
        using var host = new AssetServiceHost();
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<SpriteAsset>("sprites/missing"));

        Assert.Equal(AssetErrorKind.NotFound, exception.Kind);
    }

    [Fact]
    public async Task RawTopLevelSpriteFaultsWithRawAsset()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw(SpriteTestSupport.SpritePath, [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<SpriteAsset>(SpriteTestSupport.SpritePath));

        Assert.Equal(AssetErrorKind.RawAsset, exception.Kind);
    }

    private static async Task<AssetException> AssertInvalidAsync(
        SpriteAsset sprite,
        TextureDescriptor? descriptor = null,
        bool includeDescriptor = true,
        bool includePayload = true,
        byte[]? payload = null)
    {
        descriptor ??= SpriteTestSupport.Descriptor();

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                if (includeDescriptor)
                {
                    builder.AddMessagePack(SpriteTestSupport.TexturePath, descriptor);
                }

                if (includePayload)
                {
                    builder.AddRaw(
                        SpriteTestSupport.PayloadPath,
                        payload ?? SpriteTestSupport.Payload(descriptor));
                }

                builder.AddMessagePack(SpriteTestSupport.SpritePath, sprite);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        AssetException exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<SpriteAsset>(SpriteTestSupport.SpritePath));

        Assert.Equal(AssetErrorKind.InvalidData, exception.Kind);
        Assert.Equal(SpriteTestSupport.SpritePath, exception.AssetPath);
        return exception;
    }
}
