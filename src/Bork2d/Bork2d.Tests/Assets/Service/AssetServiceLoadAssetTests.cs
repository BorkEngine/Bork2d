using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Pack;
using Bork2d.Tests.Assets.Sprites;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

public sealed class AssetServiceLoadAssetTests
{
    [Fact]
    public async Task MessagePackEntryRoundTripsThroughTheGeneratedShape()
    {
        var value = new PackTestData { Name = "config", Value = 42, Scale = 2.5f };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddMessagePack("data/config", value));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        PackTestData loaded = await host.Service.LoadAsset<PackTestData>("data/config");

        Assert.Equal("config", loaded.Name);
        Assert.Equal(42, loaded.Value);
        Assert.Equal(2.5f, loaded.Scale);
    }

    [Fact]
    public async Task EveryCallReturnsAFreshInstance()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddMessagePack(
                "data/config",
                new PackTestData { Name = "config", Value = 1 }));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        PackTestData first = await host.Service.LoadAsset<PackTestData>("data/config");
        PackTestData second = await host.Service.LoadAsset<PackTestData>("data/config");

        Assert.NotSame(first, second);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Value, second.Value);

        first.Name = "mutated";
        Assert.Equal("config", second.Name);
    }

    [Fact]
    public async Task RawEntryFaultsWithRawAsset()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Task<PackTestData> task = host.Service.LoadAsset<PackTestData>("data/raw");
        Assert.True(task.IsFaulted);

        var exception = await Assert.ThrowsAsync<AssetException>(() => task);
        Assert.Equal(AssetErrorKind.RawAsset, exception.Kind);
        Assert.Equal("data/raw", exception.AssetPath);
    }

    [Fact]
    public async Task MissingPathFaultsWithNotFound()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Task<PackTestData> task = host.Service.LoadAsset<PackTestData>("data/missing");
        Assert.True(task.IsFaulted);

        var exception = await Assert.ThrowsAsync<AssetException>(() => task);
        Assert.Equal(AssetErrorKind.NotFound, exception.Kind);
        Assert.Equal("data/missing", exception.AssetPath);
    }

    [Fact]
    public async Task NilPayloadFaultsWithDecode()
    {
        byte[] nil = [0xC0];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/nil",
                    isRaw: false,
                    isCompressed: false,
                    offset: dataStart,
                    length: (uint)nil.Length,
                    size: (uint)nil.Length),
            ],
            nil);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("nil.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<PackTestData>("data/nil"));
        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Equal("data/nil", exception.AssetPath);
    }

    [Fact]
    public async Task DeclaredSizeMismatchesFaultWithDecode()
    {
        byte[] payload = PackSerialization.SerializeAsset(
            new PackTestData { Name = "config", Value = 3 });
        byte[] stored = PackCompression.Compress(payload);
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/short",
                    isRaw: false,
                    isCompressed: true,
                    offset: dataStart,
                    length: (uint)stored.Length,
                    size: (uint)payload.Length - 1),
                PackTestSupport.Entry(
                    "data/extra",
                    isRaw: false,
                    isCompressed: true,
                    offset: dataStart + (ulong)stored.Length,
                    length: (uint)stored.Length,
                    size: (uint)payload.Length + 1),
            ],
            stored,
            stored);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("mismatch.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var shortException = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<PackTestData>("data/short"));
        Assert.Equal(AssetErrorKind.Decode, shortException.Kind);

        var extraException = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<PackTestData>("data/extra"));
        Assert.Equal(AssetErrorKind.Decode, extraException.Kind);
    }

    [Fact]
    public async Task LoadAssetByReferenceUsesTheReferencePath()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        AssetRef<SpriteAsset> reference = SpriteTestSupport.SpritePath;
        SpriteAsset sprite = await host.Service.LoadAsset(reference);

        Assert.Equal(SpriteTestSupport.TexturePath, sprite.Texture.Path);

        DataRef data = SpriteTestSupport.PayloadPath;
        byte[] payload = await host.Service.LoadData(data);
        Assert.Equal(16, payload.Length);
    }

    [Fact]
    public void AbsentReferencesThrowArgumentException()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadAsset(default(AssetRef<PackTestData>));
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadData(default(DataRef));
        });
    }

    [Fact]
    public async Task SpriteAssetLoadsWithoutFieldOrLinkValidation()
    {
        var sprite = new SpriteAsset
        {
            Texture = default,
            Uvs = null,
            Scale = -1f,
            Topology = (SpriteTopology)99,
        };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddMessagePack(SpriteTestSupport.SpritePath, sprite));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SpriteAsset loaded = await host.Service.LoadAsset<SpriteAsset>(
            SpriteTestSupport.SpritePath);

        Assert.False(loaded.Texture.HasValue);
        Assert.Null(loaded.Uvs);
        Assert.Equal(-1f, loaded.Scale);
        Assert.Equal((SpriteTopology)99, loaded.Topology);
    }

    [Fact]
    public void InvalidPathThrowsSynchronously()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = host.Service.LoadAsset<PackTestData>(null!);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadAsset<PackTestData>("");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadAsset<PackTestData>("data/config.toml");
        });
    }

    [Fact]
    public void DisposedServiceThrowsSynchronously()
    {
        using var host = new AssetServiceHost();
        host.Start();
        host.Service.Stop();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = host.Service.LoadAsset<PackTestData>("data/config");
        });
    }
}
