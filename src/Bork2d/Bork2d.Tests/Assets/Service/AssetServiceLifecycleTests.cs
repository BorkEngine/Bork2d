using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Tests.Assets.Pack;
using Bork2d.Tests.Assets.Sprites;
using Bork2d.Tests.Assets.Vfs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests.Assets.Service;

public sealed class AssetServiceLifecycleTests
{
    [Fact]
    public void EmptyFileSystemStartsAndJoinsItsLoaderThread()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);
        Assert.True(loader.IsAlive);
        Assert.NotEqual(Environment.CurrentManagedThreadId, loader.ManagedThreadId);

        host.Service.Stop();

        Assert.Null(host.Service.LoaderThread);
        Assert.False(loader.IsAlive);
    }

    [Fact]
    public void BasePackLoadsAndServesLookups()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/raw", [1, 2, 3]);
                builder.AddMessagePack(
                    "data/config",
                    new PackTestData { Name = "config", Value = 7 });
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        Assert.True(host.Service.Exists("data/raw"));
        Assert.True(host.Service.Exists("data/config"));
        Assert.False(host.Service.Exists("data/missing"));
        Assert.Equal(
            new[] { "data/config", "data/raw" },
            host.Service.Browse().Select(info => info.Path));
        Assert.Equal("data/raw", Assert.Single(host.Service.Browse("data/raw")).Path);
    }

    [Fact]
    public void MissingBasePackThrowsAndLeavesNoLoaderThread()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string missingPath = directory.FullPath("missing.pck");

        using var host = new AssetServiceHost(missingPath);
        Assert.Throws<Bork2dException>(host.Start);

        Assert.Null(host.Service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => host.Service.Exists("data/raw"));

        var error = Assert.Single(host.LogsAt(LogLevel.Error));
        Assert.Contains(missingPath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedBasePackThrowsAndLeavesNoLoaderThread()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string malformedPath = directory.WriteBytes("malformed.pck", [1, 2, 3, 4]);

        using var host = new AssetServiceHost(malformedPath);
        Assert.Throws<Bork2dException>(host.Start);

        Assert.Null(host.Service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => host.Service.Browse());
        Assert.Single(host.LogsAt(LogLevel.Error));
    }

    [Fact]
    public void DisposeBeforeStartDisposesTheUnstartedService()
    {
        using var host = new AssetServiceHost();
        host.Service.Dispose();

        Assert.Null(host.Service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => host.Service.Exists("data/raw"));
        Assert.Throws<ObjectDisposedException>(host.Start);

        host.Service.Dispose();
    }

    [Fact]
    public void DisposeAfterStartJoinsTheLoader()
    {
        using var host = new AssetServiceHost();
        host.Start();
        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);

        host.Service.Dispose();

        Assert.False(loader.IsAlive);
        Assert.Null(host.Service.LoaderThread);
        Assert.Throws<ObjectDisposedException>(() => host.Service.Browse());
    }

    [Fact]
    public void StopIsIdempotentAndDisposeStaysSafe()
    {
        using var host = new AssetServiceHost();
        host.Start();
        Thread loader = Assert.IsType<Thread>(host.Service.LoaderThread);

        host.Service.Stop();
        host.Service.Stop();
        host.Service.Dispose();
        host.Service.Dispose();

        Assert.False(loader.IsAlive);
        Assert.Null(host.Service.LoaderThread);
    }

    [Fact]
    public void SecondStartThrowsInvalidOperation()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Throws<InvalidOperationException>(host.Start);

        host.Service.Stop();
    }

    [Fact]
    public void EveryMemberThrowsAfterStop()
    {
        using var host = new AssetServiceHost();
        host.Start();
        host.Service.Stop();

        Assert.Throws<ObjectDisposedException>(() => host.Service.LoadPack("overlay.pck"));
        Assert.Throws<ObjectDisposedException>(() => host.Service.Browse());
        Assert.Throws<ObjectDisposedException>(() => host.Service.Browse("data"));
        Assert.Throws<ObjectDisposedException>(() => host.Service.Exists("data/raw"));
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = host.Service.LoadAsset<SpriteAsset>("sprites/player");
        });
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = host.Service.LoadData("data/raw");
        });
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = host.Service.LoadAsset<PackTestData>("data/config");
        });
    }

    [Fact]
    public async Task LoadPackMountsAnOverlayAboveTheBaseAndLaterWins()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/base", [1]);
                builder.AddRaw("data/shared", [2]);
            });
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                builder.AddRaw("data/overlay", [3]);
                builder.AddRaw("data/shared", [4, 5]);
            });

        using var host = new AssetServiceHost(basePath);
        host.Start();

        host.Service.LoadPack(overlayPath);

        Assert.Equal(new byte[] { 4, 5 }, await host.Service.LoadData("data/shared"));
        Assert.Equal(
            new[] { "data/base", "data/overlay", "data/shared" },
            host.Service.Browse().Select(info => info.Path));
        Assert.Equal(overlayPath, Assert.Single(host.Service.Browse("data/shared")).PackPath);
    }

    [Fact]
    public async Task BadOverlayLeavesTheStackUnchanged()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/shared", [2]));
        string malformedPath = directory.WriteBytes("malformed.pck", [9, 9, 9, 9]);

        using var host = new AssetServiceHost(basePath);
        host.Start();

        Assert.Throws<Bork2dException>(() => host.Service.LoadPack(malformedPath));

        Assert.Single(host.Service.Browse());
        Assert.Equal(new byte[] { 2 }, await host.Service.LoadData("data/shared"));
        Assert.Single(host.LogsAt(LogLevel.Error));
    }

    [Fact]
    public async Task LoadAssetSpriteReturnsThePackedAssetWithoutRuntimeValidation()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = SpriteTestSupport.WriteSpritePack(directory);

        using var host = new AssetServiceHost(packPath);
        host.Start();

        SpriteAsset sprite = await host.Service
            .LoadAsset<SpriteAsset>(SpriteTestSupport.SpritePath)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(SpriteTestSupport.TexturePath, sprite.Texture.Path);
        Assert.Equal(new Vector2(16f, 16f), sprite.Scale);
        Assert.Equal(SpriteTopology.Quad, sprite.Topology);

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = host.Service.LoadAsset<SpriteAsset>(null!);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            _ = host.Service.LoadAsset<SpriteAsset>("sprites/player.toml");
        });
    }

    [Fact]
    public void LoadPackValidatesItsFilesystemPath()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Assert.Throws<ArgumentNullException>(() => host.Service.LoadPack(null!));
        Assert.Throws<ArgumentException>(() => host.Service.LoadPack(""));
        Assert.Throws<ArgumentException>(() => host.Service.LoadPack("   "));
    }
}
