using Bork2d.Api;
using Bork2d.Assets.Pack;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

public sealed class PackCompressionTests
{
    private static readonly byte[] _zstdMagic = [0x28, 0xB5, 0x2F, 0xFD];

    [Theory]
    [InlineData(PackCompressionMode.Debug)]
    [InlineData(PackCompressionMode.Release)]
    public void HeaderIsAlwaysZstdWithoutAChecksum(PackCompressionMode mode)
    {
        var builder = new PackBuilder(mode);
        builder.AddRaw("data/raw", [1, 2, 3]);

        byte[] pack = builder.Build();
        int headerStart = PackTestSupport.PrefixLength;
        int headerLength = (int)PackTestSupport.HeaderCompressedLength(pack);
        ReadOnlySpan<byte> header = pack.AsSpan(headerStart, headerLength);

        Assert.True(HasZstdFrameWithoutChecksum(header));
    }

    [Fact]
    public void CompressedPayloadHasNoChecksum()
    {
        var builder = new PackBuilder(PackCompressionMode.Release);
        builder.AddRaw("data/compressible", new byte[4096]);

        byte[] pack = builder.Build();
        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        PackEntry entry = Assert.Single(archive.Entries);

        Assert.True(entry.IsCompressed);
        ReadOnlySpan<byte> stored = pack.AsSpan((int)entry.Offset, (int)entry.Length);
        Assert.True(HasZstdFrameWithoutChecksum(stored));
    }

    [Fact]
    public void LazyExactSizeEnforcementRejectsShorterPayloads()
    {
        byte[] raw = new byte[1024];
        byte[] compressed = PackCompression.Compress(raw);
        byte[] valid = [1, 2, 3];
        byte[] pack = BuildPairPack(
            compressed,
            valid,
            declaredSize: (uint)raw.Length + 1);

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        PackEntry corrupt = GetEntry(archive, "data/corrupt");
        PackEntry validEntry = GetEntry(archive, "data/valid");

        Assert.Equal(valid, archive.ReadDecompressedBytes(validEntry));
        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => archive.ReadDecompressedBytes(corrupt));
        Assert.Contains("declared", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(valid, archive.ReadDecompressedBytes(validEntry));
    }

    [Fact]
    public void LazyExactSizeEnforcementRejectsLongerPayloads()
    {
        byte[] raw = new byte[1024];
        byte[] compressed = PackCompression.Compress(raw);
        byte[] valid = [1, 2, 3];
        byte[] pack = BuildPairPack(
            compressed,
            valid,
            declaredSize: (uint)raw.Length - 1);

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        PackEntry corrupt = GetEntry(archive, "data/corrupt");

        Assert.Throws<Bork2dException>(() => archive.ReadDecompressedBytes(corrupt));
    }

    [Fact]
    public void LazyReadRejectsCorruptCompressedBytes()
    {
        byte[] corrupt = [1, 2, 3];
        byte[] valid = [1, 2, 3];
        byte[] pack = BuildPairPack(corrupt, valid, declaredSize: 10);

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        PackEntry entry = GetEntry(archive, "data/corrupt");

        Assert.Throws<Bork2dException>(() => archive.ReadDecompressedBytes(entry));
    }

    [Fact]
    public void OpenDoesNotDecompressEveryPayload()
    {
        byte[] raw = new byte[4096];
        byte[] compressed = PackCompression.Compress(raw);
        byte[] valid = [9, 8, 7];
        byte[] pack = BuildPairPack(
            compressed,
            valid,
            declaredSize: (uint)raw.Length + 5);

        // Opening succeeds even though the first compressed payload violates its declared size.
        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(valid, archive.ReadDecompressedBytes(GetEntry(archive, "data/valid")));
        Assert.Throws<Bork2dException>(
            () => archive.ReadDecompressedBytes(GetEntry(archive, "data/corrupt")));
    }

    [Fact]
    public void StreamingACompressedPayloadEnforcesTheDeclaredSize()
    {
        byte[] raw = new byte[2048];
        byte[] compressed = PackCompression.Compress(raw);
        byte[] pack = BuildPairPack(
            compressed,
            [1],
            declaredSize: (uint)raw.Length - 1);

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        using Stream stream = archive.OpenDecompressedStream(GetEntry(archive, "data/corrupt"));
        byte[] buffer = new byte[4096];

        Assert.Throws<Bork2dException>(() => stream.Read(buffer));
    }

    [Fact]
    public void LargePayloadStreamsAndRoundTrips()
    {
        byte[] payload = new byte[8 * 1024 * 1024];
        var builder = new PackBuilder(PackCompressionMode.Release);
        builder.AddRaw("textures/raw/large", payload);

        using var destination = new MemoryStream();
        builder.Write(destination);
        byte[] pack = destination.ToArray();

        using PackArchive archive = PackArchive.Open(new MemoryStream(pack));
        PackEntry entry = Assert.Single(archive.Entries);
        Assert.True(entry.IsCompressed);
        Assert.True(entry.Length < entry.Size);
        Assert.Equal(payload, archive.ReadDecompressedBytes(entry));
    }

    [Fact]
    public void BuildAndStreamingWriteHaveTheSameStructure()
    {
        var builder = new PackBuilder(PackCompressionMode.Release);
        builder.AddRaw("data/raw", [1, 2, 3, 4]);
        builder.AddMessagePack(
            "data/config",
            new PackTestData { Name = "config", Value = 9, Scale = 0.5f });
        builder.AddRaw("data/compressible", new byte[1024]);

        byte[] built = builder.Build();
        using var destination = new MemoryStream();
        builder.Write(destination);

        using PackArchive builtArchive = PackArchive.Open(new MemoryStream(built));
        using PackArchive writtenArchive = PackArchive.Open(new MemoryStream(destination.ToArray()));

        Assert.Equal(
            builtArchive.Entries.Select(Describe).ToArray(),
            writtenArchive.Entries.Select(Describe).ToArray());
    }

    private static string Describe(PackEntry entry)
        => $"{entry.Path}|{entry.IsRaw}|{entry.IsCompressed}|{entry.Length}|{entry.Size}";

    private static PackEntry GetEntry(PackArchive archive, string path)
    {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        return entry;
    }

    private static byte[] BuildPairPack(byte[] corruptStored, byte[] validPayload, uint declaredSize)
    {
        return PackTestSupport.BuildPack(
            dataStart =>
            [
                PackTestSupport.Entry(
                    "data/corrupt",
                    isRaw: false,
                    isCompressed: true,
                    offset: dataStart,
                    length: (uint)corruptStored.Length,
                    size: declaredSize),
                PackTestSupport.Entry(
                    "data/valid",
                    isRaw: true,
                    offset: dataStart + (ulong)corruptStored.Length,
                    length: (uint)validPayload.Length,
                    size: (uint)validPayload.Length),
            ],
            corruptStored,
            validPayload);
    }

    private static bool HasZstdFrameWithoutChecksum(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || !data[..4].SequenceEqual(_zstdMagic))
        {
            return false;
        }

        const byte contentChecksumFlag = 0x04;
        return (data[4] & contentChecksumFlag) == 0;
    }
}
