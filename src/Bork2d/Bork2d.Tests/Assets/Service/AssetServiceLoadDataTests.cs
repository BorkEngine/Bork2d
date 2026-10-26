using Bork2d.Api.Assets;
using Bork2d.Assets.Loading;
using Bork2d.Assets.Pack;
using Bork2d.Tests.Assets.Pack;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

public sealed class AssetServiceLoadDataTests
{
    [Fact]
    public async Task RawAndMessagePackEntriesReturnTheirDecodedBytes()
    {
        byte[] raw = [1, 2, 3, 4];
        var value = new PackTestData { Name = "config", Value = 7, Scale = 1.5f };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/raw", raw);
                builder.AddMessagePack("data/config", value);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Assert.Equal(raw, await host.Service.LoadData("data/raw"));

        byte[] messagePack = await host.Service.LoadData("data/config");
        PackTestData? roundTrip = PackSerialization.DeserializeAsset<PackTestData>(
            new MemoryStream(messagePack, writable: false));
        Assert.NotNull(roundTrip);
        Assert.Equal("config", roundTrip.Name);
        Assert.Equal(7, roundTrip.Value);
        Assert.Equal(1.5f, roundTrip.Scale);
    }

    [Fact]
    public async Task CompressedPayloadIsDecompressed()
    {
        byte[] payload = new byte[4096];

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "release.pck",
            builder => builder.AddRaw("data/blob", payload),
            PackCompressionMode.Release);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        AssetInfo info = Assert.Single(host.Service.Browse("data/blob"));
        Assert.True(info.IsCompressed);
        Assert.True(info.Size > info.Length);

        Assert.Equal(payload, await host.Service.LoadData("data/blob"));
    }

    [Fact]
    public async Task EveryCallReturnsAFreshArray()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [10, 20, 30]));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        byte[] first = await host.Service.LoadData("data/raw");
        byte[] second = await host.Service.LoadData("data/raw");

        Assert.NotSame(first, second);
        Assert.Equal(first, second);

        first[0] = 99;
        Assert.Equal(new byte[] { 10, 20, 30 }, second);
    }

    [Fact]
    public async Task MissingPathFaultsWithNotFoundWithoutThrowingSynchronously()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Task<byte[]> task = host.Service.LoadData("data/missing");
        Assert.True(task.IsFaulted);

        var exception = await Assert.ThrowsAsync<AssetException>(() => task);
        Assert.Equal(AssetErrorKind.NotFound, exception.Kind);
        Assert.Equal("data/missing", exception.AssetPath);
    }

    [Fact]
    public async Task DeclaredSizeAboveArrayMaxLengthFaultsWithInvalidData()
    {
        byte[] stored = PackCompression.Compress([1, 2, 3]);
        uint declaredSize = checked((uint)Array.MaxLength + 1);
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/huge",
                    isRaw: true,
                    isCompressed: true,
                    offset: dataStart,
                    length: (uint)stored.Length,
                    size: declaredSize),
            ],
            stored);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("huge.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadData("data/huge"));
        Assert.Equal(AssetErrorKind.InvalidData, exception.Kind);
        Assert.Equal("data/huge", exception.AssetPath);
    }

    [Fact]
    public async Task CorruptCompressedPayloadFaultsWithDecode()
    {
        byte[] stored = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/corrupt",
                    isRaw: true,
                    isCompressed: true,
                    offset: dataStart,
                    length: (uint)stored.Length,
                    size: 16),
            ],
            stored);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("corrupt.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadData("data/corrupt"));
        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Equal("data/corrupt", exception.AssetPath);
    }

    [Fact]
    public void IoFailuresReportThePackPathAndDecodeFailuresTheCanonicalPath()
    {
        const string packPath = @"C:\packs\base.pck";

        var ioException = Assert.IsType<AssetException>(
            AssetFailures.Map("data/raw", packPath, new IOException("disk")));
        Assert.Equal(AssetErrorKind.Io, ioException.Kind);
        Assert.Equal(packPath, ioException.AssetPath);

        var decodeException = Assert.IsType<AssetException>(
            AssetFailures.Map("data/raw", packPath, new InvalidDataException("bad payload")));
        Assert.Equal(AssetErrorKind.Decode, decodeException.Kind);
        Assert.Equal("data/raw", decodeException.AssetPath);
    }

    [Fact]
    public void InvalidPathThrowsSynchronously()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = host.Service.LoadData(null!);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadData("");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadData("data/raw.png");
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadData("/data/raw");
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
            _ = host.Service.LoadData("data/raw");
        });
    }
}
