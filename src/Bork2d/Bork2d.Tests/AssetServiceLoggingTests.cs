using Bork2d.Api;
using Bork2d.Assets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bork2d.Tests;

public sealed class AssetServiceLoggingTests
{
    [Fact]
    public void StartLogsPackModeWithResolvedPathAndCounts()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/raw", [1, 2, 3]);
                builder.AddMessagePack("data/config", new PackTestData { Name = "config" });
            });

        using var host = new AssetServiceHost(packPath);
        host.Start();

        var start = Assert.Single(
            host.Logs,
            log => log.Level == LogLevel.Information
                && log.Message.Contains("Asset service started", StringComparison.Ordinal));
        Assert.Equal("Bork2d.Assets", start.Category);
        Assert.Contains("mode Pack", start.Message, StringComparison.Ordinal);
        Assert.Contains(packPath, start.Message, StringComparison.Ordinal);
        Assert.Contains("version 1", start.Message, StringComparison.Ordinal);
        Assert.Contains("entries 2", start.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartLogsEmptyModeForAnEmptyFileSystem()
    {
        using var host = new AssetServiceHost();
        host.Start();

        var start = Assert.Single(
            host.Logs,
            log => log.Level == LogLevel.Information
                && log.Message.Contains("Asset service started", StringComparison.Ordinal));
        Assert.Contains("mode Empty", start.Message, StringComparison.Ordinal);
        Assert.Contains("entries 0", start.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadPackLogsTheSuppliedPathAndEntryCount()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                builder.AddRaw("data/overlay", [2]);
                builder.AddRaw("data/shared", [3]);
            });

        using var host = new AssetServiceHost(basePath);
        host.Start();
        host.Service.LoadPack(overlayPath);

        var mount = Assert.Single(
            host.Logs,
            log => log.Level == LogLevel.Information
                && log.Message.Contains("Mounted asset pack", StringComparison.Ordinal));
        Assert.Equal("Bork2d.Assets", mount.Category);
        Assert.Contains(overlayPath, mount.Message, StringComparison.Ordinal);
        Assert.Contains("2 entries", mount.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StopLogsPendingCanceledAndMountedPackCounts()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder => builder.AddRaw("data/overlay", [2]));

        using var host = new AssetServiceHost(basePath);
        host.Start();
        host.Service.LoadPack(overlayPath);
        host.Service.Stop();

        var stop = Assert.Single(
            host.Logs,
            log => log.Level == LogLevel.Information
                && log.Message.Contains("Asset service stopped", StringComparison.Ordinal));
        Assert.Equal("Bork2d.Assets", stop.Category);
        Assert.Contains("pending 0", stop.Message, StringComparison.Ordinal);
        Assert.Contains("canceled 0", stop.Message, StringComparison.Ordinal);
        Assert.Contains("mounted packs 2", stop.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupFailuresLogTheCanonicalPathAndKind()
    {
        using var host = new AssetServiceHost();
        host.Start();

        await Assert.ThrowsAsync<AssetException>(() => host.Service.LoadData("data/missing"));

        var error = Assert.Single(host.LogsAt(LogLevel.Error));
        Assert.Equal("Bork2d.Assets", error.Category);
        Assert.Contains("data/missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("kind NotFound", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Exception);
    }

    [Fact]
    public async Task DecodeFailuresLogTheCanonicalPathAndKind()
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

        await Assert.ThrowsAsync<AssetException>(() => host.Service.LoadData("data/corrupt"));

        var error = Assert.Single(host.LogsAt(LogLevel.Error));
        Assert.Contains("data/corrupt", error.Message, StringComparison.Ordinal);
        Assert.Contains("kind Decode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadSpriteMissingPathLogsAndReturnsEmpty()
    {
        using var host = new AssetServiceHost();
        host.Start();

        Sprite sprite = host.Service.LoadSprite("sprites/missing");

        Assert.Equal(Sprite.Empty, sprite);
        var log = Assert.Single(
            host.Logs,
            entry => entry.Message.Contains("sprites/missing", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, log.Level);
    }

    [Fact]
    public void BrowseAndExistsDoNotLog()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1]));

        using var host = new AssetServiceHost(packPath);
        host.Start();
        int before = host.Logs.Count;

        _ = host.Service.Browse();
        _ = host.Service.Browse("data");
        _ = host.Service.Exists("data/raw");
        _ = host.Service.Exists("data/missing");

        Assert.Equal(before, host.Logs.Count);
    }

    [Fact]
    public async Task SuccessfulLoadsLogExactlyOnceAtDebug()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string packPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1, 2, 3]));

        using var host = new AssetServiceHost(packPath);
        host.Start();

        await host.Service.LoadData("data/raw");
        await host.Service.LoadData("data/raw");

        var debug = host.Logs
            .Where(log => log.Level == LogLevel.Debug
                && log.Message.Contains("Asset loaded", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, debug.Length);
        Assert.All(debug, log => Assert.Contains(
            "kind data",
            log.Message,
            StringComparison.Ordinal));
        Assert.Empty(host.LogsAt(LogLevel.Error));
    }
}
