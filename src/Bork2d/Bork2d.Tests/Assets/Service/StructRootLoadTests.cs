using System.Text;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Pack;
using Bork2d.Tests.Assets.Vfs;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

/// <summary>
/// D-M4-11: a value-type asset root loads through the typed path, a nil/empty payload is a decode
/// fault, and <see cref="AssetRef{TAsset}"/> accepts a struct target.
/// </summary>
public sealed class StructRootLoadTests
{
    [Fact]
    public async Task StructRootRoundTripsThroughPackBuilderAndLoader()
    {
        var value = new PackTestStruct(Attack: 7, Defense: 9);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddMessagePack("data/boss", value));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        PackTestStruct loaded = await host.Service.LoadAsset<PackTestStruct>("data/boss");

        Assert.Equal(value, loaded);
    }

    [Fact]
    public async Task NilStructRootFaultsDecode()
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
            () => host.Service.LoadAsset<PackTestStruct>("data/nil"));

        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Equal("data/nil", exception.AssetPath);
        Assert.Contains("nil", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyPayloadFaultsDecode()
    {
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/empty",
                    isRaw: false,
                    isCompressed: false,
                    offset: dataStart,
                    length: 0,
                    size: 0),
            ]);

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WriteBytes("empty.pck", pack);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var exception = await Assert.ThrowsAsync<AssetException>(
            () => host.Service.LoadAsset<PackTestStruct>("data/empty"));

        Assert.Equal(AssetErrorKind.Decode, exception.Kind);
        Assert.Equal("data/empty", exception.AssetPath);
    }

    [Fact]
    public async Task AssetRefToAStructRootRoundTripsAsAPathString()
    {
        var value = new PackTestStruct(Attack: 3, Defense: 5);
        var holder = new StructReferencePackData { Boss = "data/boss" };

        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddMessagePack("data/boss", value);
                builder.AddMessagePack("data/holder", holder);
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        StructReferencePackData loaded =
            await host.Service.LoadAsset<StructReferencePackData>("data/holder");
        Assert.Equal("data/boss", loaded.Boss.Path);
        Assert.True(loaded.Boss.HasValue);

        PackTestStruct boss = await host.Service.LoadAsset(loaded.Boss);
        Assert.Equal(value, boss);

        byte[] payload = PackSerialization.SerializeAsset(holder);
        Assert.Contains("data/boss", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);
    }

    [Fact]
    public void StructReferenceToAMissingAssetFailsPackBuild()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/holder",
            new StructReferencePackData { Boss = "data/missing" });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("data/holder", exception.Message, StringComparison.Ordinal);
        Assert.Contains("data/missing", exception.Message, StringComparison.Ordinal);
    }
}
