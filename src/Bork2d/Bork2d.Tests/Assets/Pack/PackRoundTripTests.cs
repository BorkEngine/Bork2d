using Bork2d.Engine;
using Bork2d.Engine.Assets.Pack;
using Nerdbank.MessagePack;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

public sealed class PackRoundTripTests
{
    private static readonly byte[] _rawPayload = [1, 2, 3, 4, 5, 6, 7, 8];

    [Theory]
    [InlineData(PackCompressionMode.Debug)]
    [InlineData(PackCompressionMode.Release)]
    public void EmptyPackRoundTrips(PackCompressionMode mode)
    {
        byte[] pack = new PackBuilder(mode).Build();

        Assert.True(pack.Length > PackTestSupport.PrefixLength);
        Assert.Equal("BORK2DPK", System.Text.Encoding.ASCII.GetString(pack, 0, 8));
        Assert.Equal(1u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(8, 4)));

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        Assert.Empty(archive.Entries);
        Assert.False(archive.TryGetEntry("data/missing", out _));
    }

    [Theory]
    [InlineData(PackCompressionMode.Debug)]
    [InlineData(PackCompressionMode.Release)]
    public void RawPayloadRoundTrips(PackCompressionMode mode)
    {
        var builder = new PackBuilder(mode);
        builder.AddRaw("data/raw", _rawPayload);

        using PackArchive archive = OpenBuilt(builder);
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.Equal("data/raw", entry.Path);
        Assert.True(entry.IsRaw);
        Assert.Equal((uint)_rawPayload.Length, entry.Size);
        Assert.Equal(_rawPayload, archive.ReadDecompressedBytes(entry));
    }

    [Fact]
    public void DebugPayloadsAreStoredUncompressed()
    {
        byte[] payload = new byte[4096];
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/zeros", payload);

        using PackArchive archive = OpenBuilt(builder);
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.False(entry.IsCompressed);
        Assert.Equal(entry.Length, entry.Size);
        Assert.Equal(payload, archive.ReadStoredBytes(entry));
        Assert.Equal(payload, archive.ReadDecompressedBytes(entry));
    }

    [Fact]
    public void ReleaseCompressesOnlyWhenStrictlySmaller()
    {
        byte[] compressible = new byte[4096];
        byte[] incompressible = CreateIncompressiblePayload(4096);
        var builder = new PackBuilder(PackCompressionMode.Release);
        builder.AddRaw("data/compressible", compressible);
        builder.AddRaw("data/incompressible", incompressible);

        using PackArchive archive = OpenBuilt(builder);
        PackEntry compressed = GetEntry(archive, "data/compressible");
        PackEntry stored = GetEntry(archive, "data/incompressible");

        Assert.True(compressed.IsCompressed);
        Assert.True(compressed.Length < compressed.Size);
        Assert.Equal(compressible, archive.ReadDecompressedBytes(compressed));

        Assert.False(stored.IsCompressed);
        Assert.Equal(stored.Length, stored.Size);
        Assert.Equal(incompressible, archive.ReadDecompressedBytes(stored));
    }

    [Fact]
    public void MessagePackAssetRoundTripsWithTheGeneratedShape()
    {
        var original = new PackTestData { Name = "config", Value = 42, Scale = 1.5f };
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddMessagePack("data/config", original);

        using PackArchive archive = OpenBuilt(builder);
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.False(entry.IsRaw);

        PackTestData? roundTripped = new MessagePackSerializer()
            .Deserialize<PackTestData>(archive.ReadDecompressedBytes(entry));
        Assert.NotNull(roundTripped);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.Value, roundTripped.Value);
        Assert.Equal(original.Scale, roundTripped.Scale);
    }

    [Fact]
    public void EntriesAreSortedAndUseAbsoluteOffsets()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("sprites/knight", new byte[] { 1 });
        builder.AddRaw("data/zulu", new byte[] { 2, 2 });
        builder.AddRaw("data/alpha", new byte[] { 3, 3, 3 });

        byte[] pack = builder.Build();
        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));

        Assert.Equal(
            new[] { "data/alpha", "data/zulu", "sprites/knight" },
            archive.Entries.Select(entry => entry.Path).ToArray());

        ulong offset = (ulong)PackTestSupport.DataStart(pack);
        foreach (PackEntry entry in archive.Entries)
        {
            Assert.Equal(offset, entry.Offset);
            Assert.Equal(entry.Length, entry.Size);
            offset = checked(offset + entry.Length);
        }

        Assert.Equal((ulong)pack.Length, offset);
    }

    [Fact]
    public void BuildIsRepeatableAndDoesNotConsumeTheBuilder()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/one", new byte[] { 1 });

        byte[] first = builder.Build();
        byte[] second = builder.Build();
        using (PackArchive firstArchive = PackArchive.Open(new MemoryStream(first)))
        using (PackArchive secondArchive = PackArchive.Open(new MemoryStream(second)))
        {
            Assert.Equal(
                firstArchive.Entries.Select(entry => entry.Path),
                secondArchive.Entries.Select(entry => entry.Path));
        }

        builder.AddRaw("data/two", new byte[] { 2 });
        using PackArchive extended = PackArchive.Open(new MemoryStream(builder.Build()));
        Assert.Equal(2, extended.Entries.Count);
    }

    [Fact]
    public void AddRawCopiesThePayload()
    {
        byte[] payload = [1, 2, 3];
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/copied", payload);
        payload[0] = 99;

        using PackArchive archive = OpenBuilt(builder);
        Assert.Equal(new byte[] { 1, 2, 3 }, archive.ReadDecompressedBytes(Assert.Single(archive.Entries)));
    }

    [Fact]
    public void WriteStreamLeavesTheDestinationOpen()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/raw", _rawPayload);

        using var destination = new MemoryStream();
        builder.Write(destination);

        Assert.True(destination.CanWrite);
        Assert.True(destination.Length > PackTestSupport.PrefixLength);
        destination.WriteByte(0);
        Assert.True(destination.CanWrite);

        destination.Position = 0;
        using PackArchive archive = PackArchive.Open(destination);
        Assert.Equal(_rawPayload, archive.ReadDecompressedBytes(Assert.Single(archive.Entries)));
    }

    [Fact]
    public void WriteFileOverwritesAndReadsBack()
    {
        string path = CreateTempPackPath();
        try
        {
            var builder = new PackBuilder(PackCompressionMode.Release);
            builder.AddRaw("data/raw", _rawPayload);
            builder.Write(path);
            Assert.True(File.Exists(path));

            builder.AddRaw("data/second", [9, 9]);
            builder.Write(path);

            using PackArchive archive = PackArchive.Open(path);
            Assert.Equal(2, archive.Entries.Count);
            Assert.Equal(_rawPayload, archive.ReadDecompressedBytes(GetEntry(archive, "data/raw")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteResolvesRelativePathsAgainstTheCurrentDirectory()
    {
        string fullPath = CreateTempPackPath();
        string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        try
        {
            var builder = new PackBuilder(PackCompressionMode.Debug);
            builder.AddRaw("data/raw", _rawPayload);
            builder.Write(relativePath);

            Assert.True(File.Exists(fullPath));
            using PackArchive archive = PackArchive.Open(fullPath);
            Assert.Single(archive.Entries);
        }
        finally
        {
            File.Delete(fullPath);
        }
    }

    [Fact]
    public void DuplicatePathsThrow()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/path", [1]);

        Assert.Throws<Bork2dException>(() => builder.AddRaw("data/path", [2]));
        Assert.Throws<Bork2dException>(() => builder.AddRaw("DATA\\PATH", [3]));
        Assert.Throws<Bork2dException>(
            () => builder.AddMessagePack(
                "data/path",
                new PackTestData { Name = "duplicate", Value = 1, Scale = 1f }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("double//slash")]
    [InlineData("with.dot")]
    [InlineData("textures/player.png")]
    [InlineData("../escape")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("has space")]
    [InlineData("has:colon")]
    [InlineData("has*star")]
    public void InvalidPathsThrowArgumentException(string path)
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);

        Assert.Throws<ArgumentException>(() => builder.AddRaw(path, [1]));
        Assert.Throws<ArgumentException>(
            () => builder.AddMessagePack(path, new PackTestData { Name = "x", Value = 1, Scale = 1f }));
    }

    [Fact]
    public void NullPathsThrowArgumentNullException()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);

        Assert.Throws<ArgumentNullException>(() => builder.AddRaw(null!, [1]));
        Assert.Throws<ArgumentNullException>(
            () => builder.AddMessagePack(
                null!,
                new PackTestData { Name = "x", Value = 1, Scale = 1f }));
    }

    [Fact]
    public void PathInputsAreNormalizedToCanonicalForm()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("Textures\\Player\\Idle", new byte[] { 1 });

        using PackArchive archive = OpenBuilt(builder);
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.Equal("textures/player/idle", entry.Path);
        Assert.True(archive.TryGetEntry("textures/player/idle", out _));
    }

    [Fact]
    public void WriteRejectsArgumentErrors()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/raw", [1]);

        Assert.Throws<ArgumentNullException>(() => builder.Write((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => builder.Write((string)null!));
        Assert.Throws<ArgumentException>(() => builder.Write(" "));

        using var readOnly = new MemoryStream(new byte[1], writable: false);
        Assert.Throws<ArgumentException>(() => builder.Write(readOnly));
    }

    [Fact]
    public void OpenReportsArgumentErrors()
    {
        Assert.Throws<ArgumentNullException>(() => PackArchive.Open((string)null!));
        Assert.Throws<ArgumentException>(() => PackArchive.Open(" "));
        Assert.Throws<ArgumentNullException>(() => PackArchive.Open((Stream)null!));
    }

    [Fact]
    public void OpenMissingFileThrowsBork2dExceptionWithTheResolvedPath()
    {
        string path = CreateTempPackPath();

        Bork2dException exception = Assert.Throws<Bork2dException>(() => PackArchive.Open(path));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OpeningAStreamTransfersOwnership()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/raw", [1]);
        var stream = new MemoryStream(builder.Build());

        using (PackArchive archive = PackArchive.Open(stream))
        {
            Assert.Single(archive.Entries);
        }

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void EntryFromAnotherArchiveIsRejected()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/raw", [1]);
        byte[] pack = builder.Build();

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        var foreign = PackTestSupport.Entry("data/raw", isRaw: true, length: 1, size: 1);

        Assert.Throws<ArgumentException>(() => archive.ReadDecompressedBytes(foreign));
    }

    [Fact]
    public void ZeroLengthRawEntryRoundTrips()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/empty", []);

        using PackArchive archive = OpenBuilt(builder);
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.Equal(0u, entry.Length);
        Assert.Equal(0u, entry.Size);
        Assert.Empty(archive.ReadDecompressedBytes(entry));
        Assert.Empty(archive.ReadStoredBytes(entry));
    }

    [Fact]
    public void ManyEntriesRoundTripAcrossOffsetBoundaries()
    {
        const int entryCount = 2048;
        byte[] payload = new byte[64];
        var builder = new PackBuilder(PackCompressionMode.Debug);
        for (int i = 0; i < entryCount; i++)
        {
            builder.AddRaw($"data/item-{i:D4}", payload);
        }

        byte[] pack = builder.Build();
        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        Assert.Equal(entryCount, archive.Entries.Count);

        ulong offset = archive.Entries[0].Offset;
        Assert.True(offset >= (ulong)PackTestSupport.DataStart(pack));
        foreach (PackEntry entry in archive.Entries)
        {
            Assert.Equal(offset, entry.Offset);
            offset = checked(offset + entry.Length);
        }

        Assert.Equal((ulong)pack.Length, offset);
        Assert.True(offset > ushort.MaxValue);
        Assert.Equal(payload, archive.ReadDecompressedBytes(GetEntry(archive, "data/item-0500")));
    }

    [Fact]
    public void SerializationFailuresThrowBork2dException()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => builder.AddMessagePack("data/throwing", new ThrowingPackData()));

        Assert.Contains("serialize", exception.Message, StringComparison.OrdinalIgnoreCase);
        using PackArchive archive = OpenBuilt(builder);
        Assert.Empty(archive.Entries);
    }

    [Fact]
    public void WriteFailureThrowsBork2dException()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);
        builder.AddRaw("data/raw", [1]);
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"bork2d-missing-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "pack.pck");

        Bork2dException exception = Assert.Throws<Bork2dException>(() => builder.Write(path));

        Assert.Contains(Path.GetFullPath(path), exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void AddMessagePackRejectsNullValues()
    {
        var builder = new PackBuilder(PackCompressionMode.Debug);

        Assert.Throws<ArgumentNullException>(
            () => builder.AddMessagePack<PackTestData>("data/null", null!));
    }

    private static PackArchive OpenBuilt(PackBuilder builder)
        => PackArchive.Open(new MemoryStream(builder.Build(), writable: false));

    private static PackEntry GetEntry(PackArchive archive, string path)
    {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        return entry;
    }

    private static string CreateTempPackPath()
        => Path.Combine(Path.GetTempPath(), $"bork2d-pack-{Guid.NewGuid():N}.pck");

    private static byte[] CreateIncompressiblePayload(int length)
    {
        var payload = new byte[length];
        var random = new Random(12345);
        random.NextBytes(payload);
        return payload;
    }
}
