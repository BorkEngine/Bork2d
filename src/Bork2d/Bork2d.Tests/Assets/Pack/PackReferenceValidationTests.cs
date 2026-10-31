using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Tests.Assets.Vfs;
using Nerdbank.MessagePack;
using PolyType;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

public sealed class PackReferenceValidationTests
{
    [Fact]
    public void ValidReferencesInPropertiesCollectionsDictionariesAndNestedObjectsRoundTrip()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack("data/refs", FullReferences());

        using PackArchive archive = OpenBuilt(builder);
        ReferencePackData loaded = Deserialize<ReferencePackData>(archive, "data/refs");

        Assert.Equal("data/config", loaded.Asset.Path);
        Assert.Equal("data/raw", loaded.Data.Path);
        Assert.Equal("data/config", Assert.Single(loaded.Assets!).Path);
        Assert.Equal("data/raw", loaded.DataByName!["raw"].Path);
        Assert.Equal("data/config", loaded.Nested!.Asset.Path);
    }

    [Fact]
    public void MissingAssetReferenceThrowsAtBuildTime()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("data/missing"),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("data/refs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Asset", exception.Message, StringComparison.Ordinal);
        Assert.Contains("data/missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetReferenceToRawEntryThrowsAtBuildTime()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("data/raw"),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("data/raw", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MessagePack asset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataReferenceToMessagePackEntryThrowsAtBuildTime()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Data = new DataRef("data/config"),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("data/config", exception.Message, StringComparison.Ordinal);
        Assert.Contains("raw data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullReferencesAreValid()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack("data/refs", new ReferencePackData());

        using PackArchive archive = OpenBuilt(builder);
        ReferencePackData loaded = Deserialize<ReferencePackData>(archive, "data/refs");

        Assert.False(loaded.Asset.HasValue);
        Assert.False(loaded.Data.HasValue);
        Assert.Null(loaded.Assets);
        Assert.Null(loaded.DataByName);
        Assert.Null(loaded.Nested);
    }

    [Fact]
    public void EmptyReferencePathThrowsAtBuildTime()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>(string.Empty),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("empty reference path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonCanonicalReferenceThrowsAtBuildTime()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("Data\\Config"),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("non-canonical", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedWriteDoesNotCreateTheFile()
    {
        using TempPackDirectory directory = TempPackDirectory.CreateUnderTemp();
        string path = directory.FullPath("invalid.pck");
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("data/missing"),
            });

        Assert.Throws<Bork2dException>(() => builder.Write(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AddingTheReferencedEntryAfterAFailedBuildFixesThePack()
    {
        var builder = CreateBaseBuilder();
        builder.AddMessagePack(
            "data/refs",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("data/later"),
            });

        Assert.Throws<Bork2dException>(() => builder.Build());

        builder.AddMessagePack("data/later", new PackTestData { Name = "later", Value = 3 });

        using PackArchive archive = OpenBuilt(builder);
        Assert.True(archive.TryGetEntry("data/later", out _));
        Assert.True(archive.TryGetEntry("data/refs", out _));
    }

    [Fact]
    public void ExternalMainAssetAllowsADlcReferenceWithoutChangingThePack()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/dlc",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("art/player"),
            });
        builder.SetExternalAssets([new PackExternalAsset("art/player", IsRaw: false)]);

        using PackArchive archive = OpenBuilt(builder);
        Assert.True(archive.TryGetEntry("data/dlc", out _));
        Assert.False(archive.TryGetEntry("art/player", out _));
    }

    [Fact]
    public void ExternalRawAssetAllowsADlcDataReference()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/dlc",
            new ReferencePackData
            {
                Data = new DataRef("art/player-raw"),
            });
        builder.SetExternalAssets([new PackExternalAsset("art/player-raw", IsRaw: true)]);

        using PackArchive archive = OpenBuilt(builder);
        Assert.True(archive.TryGetEntry("data/dlc", out _));
    }

    [Fact]
    public void ExternalMainAssetStillEnforcesTheReferenceKind()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/dlc",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("art/player-raw"),
            });
        builder.SetExternalAssets([new PackExternalAsset("art/player-raw", IsRaw: true)]);

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("external asset", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MessagePack asset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingExternalAssetFailsWithoutAReferenceTarget()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack(
            "data/dlc",
            new ReferencePackData
            {
                Asset = new AssetRef<PackTestData>("art/player"),
            });

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Build());

        Assert.Contains("not present in the pack", exception.Message, StringComparison.Ordinal);
    }

    private static PackBuilder CreateBaseBuilder()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack("data/config", new PackTestData { Name = "config", Value = 1 });
        builder.AddRaw("data/raw", [1, 2, 3]);
        return builder;
    }

    private static ReferencePackData FullReferences() => new()
    {
        Asset = new AssetRef<PackTestData>("data/config"),
        Data = new DataRef("data/raw"),
        Assets = [new AssetRef<PackTestData>("data/config")],
        DataByName = new Dictionary<string, DataRef>
        {
            ["raw"] = new DataRef("data/raw"),
        },
        Nested = new ReferenceNested
        {
            Asset = new AssetRef<PackTestData>("data/config"),
        },
    };

    private static PackArchive OpenBuilt(PackBuilder builder)
        => PackArchive.Open(new MemoryStream(builder.Build(), writable: false));

    private static T Deserialize<T>(PackArchive archive, string path)
        where T : IShapeable<T>
    {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        return new MessagePackSerializer().Deserialize<T>(archive.ReadDecompressedBytes(entry!))!;
    }
}
