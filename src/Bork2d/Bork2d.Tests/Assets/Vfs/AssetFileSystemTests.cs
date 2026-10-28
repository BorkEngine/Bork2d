using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Assets.Pack;
using Bork2d.Engine.Assets.Vfs;
using Bork2d.Tests.Assets.Pack;
using Xunit;

namespace Bork2d.Tests.Assets.Vfs;

public sealed class AssetFileSystemTests
{
    [Fact]
    public void EmptyFileSystemBrowsesEmptyAndMissesValidPaths()
    {
        using var fileSystem = new AssetFileSystem();

        Assert.Empty(fileSystem.Browse());
        Assert.Empty(fileSystem.Browse("data"));
        Assert.False(fileSystem.Exists("data/raw"));
        Assert.False(fileSystem.Exists("Textures\\Player"));
        Assert.False(fileSystem.TryGetEntry("data/raw", out _));
    }

    [Fact]
    public void ExistsValidatesItsPathArgument()
    {
        using var fileSystem = new AssetFileSystem();

        Assert.Throws<ArgumentNullException>(() => fileSystem.Exists(null!));
        Assert.Throws<ArgumentException>(() => fileSystem.Exists(""));
        Assert.Throws<ArgumentException>(() => fileSystem.Exists("data/raw.png"));
        Assert.Throws<ArgumentException>(() => fileSystem.Exists("/data/raw"));
        Assert.Throws<ArgumentException>(() => fileSystem.Exists("data//raw"));
    }

    [Fact]
    public void BrowseRejectsAnEmptyOrInvalidPrefix()
    {
        using var fileSystem = new AssetFileSystem();

        Assert.Throws<ArgumentException>(() => fileSystem.Browse(""));
        Assert.Throws<ArgumentException>(() => fileSystem.Browse("data/raw.png"));
        Assert.Throws<ArgumentException>(() => fileSystem.Browse("/data"));
        Assert.Throws<ArgumentException>(() => fileSystem.Browse("data//raw"));
        Assert.Empty(fileSystem.Browse(null));
        Assert.Empty(fileSystem.Browse("data"));
    }

    [Fact]
    public void LoadPackValidatesItsFilesystemPath()
    {
        using var fileSystem = new AssetFileSystem();

        Assert.Throws<ArgumentNullException>(() => fileSystem.LoadPack(null!));
        Assert.Throws<ArgumentException>(() => fileSystem.LoadPack(""));
        Assert.Throws<ArgumentException>(() => fileSystem.LoadPack("   "));
    }

    [Fact]
    public void LoadPackFromAnAbsolutePathMakesItsEntriesVisible()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string path = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1, 2, 3]));

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        Assert.True(fileSystem.Exists("data/raw"));
        Assert.False(fileSystem.Exists("data/missing"));

        AssetInfo info = fileSystem.Browse().Single();
        Assert.Equal("data/raw", info.Path);
        Assert.True(info.IsRaw);
        Assert.False(info.IsCompressed);
        Assert.Equal(3L, info.Length);
        Assert.Equal(3L, info.Size);
        Assert.Equal(path, info.PackPath);
    }

    [Fact]
    public void LoadPackResolvesRelativePathsAgainstTheCurrentDirectory()
    {
        using var directory = TempPackDirectory.CreateUnderCurrentDirectory();
        string fullPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [7]));
        string suppliedPath = directory.SuppliedPath("base.pck");
        Assert.False(Path.IsPathRooted(suppliedPath));

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(suppliedPath);

        Assert.True(fileSystem.Exists("data/raw"));
        Assert.Equal(suppliedPath, fileSystem.Browse().Single().PackPath);
        Assert.NotEqual(fullPath, fileSystem.Browse().Single().PackPath);
    }

    [Fact]
    public void MissingOverlayThrowsAndLeavesTheStackUnchanged()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        string missingPath = directory.FullPath("missing.pck");

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(basePath);
        AssetInfo[] before = fileSystem.Browse().ToArray();

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fileSystem.LoadPack(missingPath));

        Assert.Contains(
            Path.GetFullPath(missingPath),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.Browse().ToArray());
        Assert.True(fileSystem.Exists("data/base"));
        Assert.False(fileSystem.Exists("data/overlay"));
    }

    [Fact]
    public void MalformedOverlayThrowsAndLeavesTheStackUnchanged()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/base", [1]));
        string malformedPath = directory.WriteBytes("bad.pck", [1, 2, 3, 4]);

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(basePath);
        AssetInfo[] before = fileSystem.Browse().ToArray();

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => fileSystem.LoadPack(malformedPath));

        Assert.Contains(
            Path.GetFullPath(malformedPath),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(before, fileSystem.Browse().ToArray());
        Assert.Single(fileSystem.Browse());
    }

    [Fact]
    public void PackWithDuplicatePathsIsRejectedAndNotMounted()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        byte[] pack = PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry("data/raw", offset: dataStart, length: 1, size: 1),
                PackTestSupport.Entry("data/raw", offset: dataStart, length: 1, size: 1),
            ],
            [9]);
        string path = directory.WriteBytes("duplicate.pck", pack);

        using var fileSystem = new AssetFileSystem();

        Assert.Throws<Bork2dException>(() => fileSystem.LoadPack(path));
        Assert.Empty(fileSystem.Browse());
    }

    [Fact]
    public void EmptyPackMountsAsAnEmptyFileSystem()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string path = directory.WritePack("empty.pck", _ => { });

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        Assert.Empty(fileSystem.Browse());
        Assert.False(fileSystem.Exists("data/raw"));
    }

    [Fact]
    public void LaterMountsShadowEarlierPaths()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string basePath = directory.WritePack(
            "base.pck",
            builder =>
            {
                builder.AddRaw("data/base", [2]);
                builder.AddRaw("data/shared", [1]);
            });
        string overlayPath = directory.WritePack(
            "overlay.pck",
            builder =>
            {
                builder.AddRaw("data/overlay", [5]);
                builder.AddRaw("data/shared", [3, 4]);
            });
        string topPath = directory.WritePack(
            "top.pck",
            builder => builder.AddRaw("data/shared", [6, 7, 8]));

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(basePath);
        fileSystem.LoadPack(overlayPath);
        fileSystem.LoadPack(topPath);

        AssetInfo shared = fileSystem.Browse("data/shared").Single();
        Assert.Equal("data/shared", shared.Path);
        Assert.Equal(topPath, shared.PackPath);
        Assert.Equal(3L, shared.Length);
        Assert.True(fileSystem.TryGetEntry("data/shared", out ResolvedAsset? entry));
        Assert.Equal(new byte[] { 6, 7, 8 }, entry.ReadDecompressedBytes());
        Assert.Equal(
            new[] { "data/base", "data/overlay", "data/shared" },
            fileSystem.Browse().Select(info => info.Path));
    }

    [Fact]
    public void MountingTheSameFileTwiceUsesTheLaterSuppliedIdentifier()
    {
        using var directory = TempPackDirectory.CreateUnderCurrentDirectory();
        string fullPath = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1]));
        string suppliedPath = directory.SuppliedPath("base.pck");

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(fullPath);
        fileSystem.LoadPack(suppliedPath);

        AssetInfo info = fileSystem.Browse().Single();
        Assert.Equal(suppliedPath, info.PackPath);
        Assert.Single(fileSystem.Browse());
    }

    [Fact]
    public void BrowseFiltersOnSlashSegmentBoundaries()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string[] paths =
        [
            "sprites/pl",
            "sprites/pl/idle",
            "sprites/pl2",
            "sprites/player",
            "sprites/player/idle",
            "textures/player",
        ];
        string path = directory.WritePack(
            "sprites.pck",
            builder =>
            {
                foreach (string assetPath in paths)
                {
                    builder.AddRaw(assetPath, [1]);
                }
            });

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        Assert.Equal(
            new[] { "sprites/pl", "sprites/pl/idle" },
            fileSystem.Browse("sprites/pl").Select(info => info.Path));
        Assert.Equal(
            new[] { "sprites/player", "sprites/player/idle" },
            fileSystem.Browse("sprites/player").Select(info => info.Path));
        Assert.Equal(
            paths
                .Where(name => name.StartsWith("sprites/", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal),
            fileSystem.Browse("sprites").Select(info => info.Path));
        Assert.Equal(
            new[] { "sprites/pl2" },
            fileSystem.Browse("sprites/pl2").Select(info => info.Path));
        Assert.Empty(fileSystem.Browse("sprites/pl3"));
        Assert.Equal(paths.Length, fileSystem.Browse(null).Count);
        Assert.Equal(paths.Length, fileSystem.Browse().Count);
    }

    [Fact]
    public void ExistsIsAnExactCanonicalLookup()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string path = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("sprites/player", [1]));

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        Assert.True(fileSystem.Exists("sprites/player"));
        Assert.True(fileSystem.Exists("Sprites\\Player"));
        Assert.False(fileSystem.Exists("sprites/pl"));
        Assert.False(fileSystem.Exists("sprites"));
        Assert.False(fileSystem.Exists("sprites/player/idle"));
    }

    [Fact]
    public void AssetInfoReportsHeaderFieldsAndTheSuppliedPackIdentifier()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        byte[] raw = [1, 2, 3, 4, 5];
        string path = directory.WritePack(
            "mixed.pck",
            builder =>
            {
                builder.AddRaw("data/raw", raw);
                builder.AddMessagePack(
                    "data/config",
                    new PackTestData { Name = "config", Value = 7 });
            });

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        AssetInfo rawInfo = fileSystem.Browse("data/raw").Single();
        Assert.True(rawInfo.IsRaw);
        Assert.False(rawInfo.IsCompressed);
        Assert.Equal(5L, rawInfo.Length);
        Assert.Equal(5L, rawInfo.Size);
        Assert.Equal(path, rawInfo.PackPath);

        AssetInfo configInfo = fileSystem.Browse("data/config").Single();
        Assert.False(configInfo.IsRaw);
        Assert.False(configInfo.IsCompressed);
        Assert.True(configInfo.Size > 0);
        Assert.Equal(configInfo.Size, configInfo.Length);
        Assert.Equal(path, configInfo.PackPath);
    }

    [Fact]
    public void AssetInfoReportsCompressedReleasePayloads()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        byte[] payload = new byte[4096];
        string path = directory.WritePack(
            "release.pck",
            builder => builder.AddRaw("data/zeros", payload),
            PackCompressionMode.Release);

        using var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);

        AssetInfo info = fileSystem.Browse().Single();
        Assert.True(info.IsRaw);
        Assert.True(info.IsCompressed);
        Assert.True(info.Length < info.Size);
        Assert.Equal(4096L, info.Size);
        Assert.Equal(path, info.PackPath);

        Assert.True(fileSystem.TryGetEntry("data/zeros", out ResolvedAsset? entry));
        byte[] decoded = entry.ReadDecompressedBytes();
        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void DisposeClosesMountedArchivesAndRejectsFurtherUse()
    {
        using var directory = TempPackDirectory.CreateUnderTemp();
        string path = directory.WritePack(
            "base.pck",
            builder => builder.AddRaw("data/raw", [1]));

        var fileSystem = new AssetFileSystem();
        fileSystem.LoadPack(path);
        Assert.True(fileSystem.TryGetEntry("data/raw", out ResolvedAsset? entry));
        Assert.Equal(new byte[] { 1 }, entry.ReadDecompressedBytes());

        fileSystem.Dispose();
        fileSystem.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = fileSystem.Exists("data/raw"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = fileSystem.Browse(); });
        Assert.Throws<ObjectDisposedException>(
            () => { _ = fileSystem.TryGetEntry("data/raw", out _); });
        Assert.Throws<ObjectDisposedException>(() => fileSystem.LoadPack(path));
        Assert.Throws<ObjectDisposedException>(() => { _ = entry.ReadDecompressedBytes(); });
        Assert.Throws<ObjectDisposedException>(
            () => { using Stream stream = entry.OpenStoredStream(); });
    }
}
